using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TaskManagement.Domain.Access;
using TaskManagement.IntegrationTests.Infrastructure;
using TaskManagement.IntegrationTests.Projects;

namespace TaskManagement.IntegrationTests.Tasks;

[Collection(ApiCollection.Name)]
public sealed class TaskTests(ApiFactory factory)
{
    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    private static object Edit(JsonElement task, string title, string? dueDate = null, bool? algorithmRequired = null) => new
    {
        title,
        description = task.GetProperty("description").GetString(),
        type = task.GetProperty("type").GetString(),
        priority = task.GetProperty("priority").GetString(),
        dueDate = dueDate ?? task.GetProperty("dueDate").GetString(),
        allocatedMinutes = 240,
        algorithmRequired,
        labels = new[] { "backend" },
        version = task.GetProperty("version").GetUInt32(),
    };

    [Fact]
    public async Task AC_TASK_1_a_task_without_a_project_is_rejected()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);

        var response = await scenario.Pm.PostAsync("/api/v1/tasks", new { title = "Orphan" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.JsonAsync()).GetProperty("errors").TryGetProperty("projectId", out _));
    }

    [Fact]
    public async Task Tasks_get_sequential_keys_even_when_created_at_the_same_time()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);

        var created = await Task.WhenAll(Enumerable.Range(1, 5).Select(i => scenario.CreateTaskAsync($"Parallel {i}")));

        Assert.Equal(
            Enumerable.Range(1, 5).Select(n => $"{scenario.Key}-{n}"),
            created.Select(t => t.GetProperty("key").GetString()).Order(StringComparer.Ordinal));
        var first = created.Single(t => t.GetProperty("key").GetString() == $"{scenario.Key}-1");
        Assert.Equal("Draft", first.GetProperty("status").GetString());
        Assert.Equal(0, first.GetProperty("currentRevision").GetInt32());
        Assert.Equal(Id(first), Id(await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/tasks/by-key/{scenario.Key.ToLowerInvariant()}-1")));
    }

    [Fact]
    public async Task Tasks_need_an_active_team_leader_or_project_manager_in_the_project()
    {
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);
        var project = await (await admin.PostAsync("/api/v1/projects", new { key = ProjectScenario.NewKey(), name = "Leaderless" })).JsonAsync();
        var role = await (await admin.PostAsync("/api/v1/roles", new { name = "Planner " + Guid.NewGuid().ToString("N")[..6], description = "" })).JsonAsync();
        await admin.PutAsync($"/api/v1/roles/{Id(role)}/permissions", new
        {
            permissions = new[] { new { code = "projects.view", scope = "Project" }, new { code = "tasks.view", scope = "Project" }, new { code = "tasks.create", scope = "Project" } },
            version = role.GetProperty("version").GetUInt32(),
        });
        var planner = await factory.CreateMemberAsync(SystemRoles.DeveloperId);
        var lead = await factory.CreateMemberAsync(SystemRoles.TeamLeaderId);
        using var plannerClient = factory.CreateTestClient();
        await plannerClient.SignInAsync(planner.Email, planner.Password);
        await admin.PostAsync($"/api/v1/projects/{Id(project)}/members", new { userId = planner.Id, projectRoleId = Id(role) });
        var tasksPath = $"/api/v1/projects/{Id(project)}/tasks";

        var asAdmin = await admin.PostAsync(tasksPath, new { title = "No lead" });
        var withoutLead = await plannerClient.PostAsync(tasksPath, new { title = "No lead" });
        await admin.PostAsync($"/api/v1/projects/{Id(project)}/members", new { userId = lead.Id, projectRoleId = SystemRoles.TeamLeaderId });
        var withLead = await plannerClient.PostAsync(tasksPath, new { title = "Now it works" });

        Assert.Equal(HttpStatusCode.Forbidden, asAdmin.StatusCode); // Admin is not a TL/PM (permissions matrix)
        Assert.Equal("project.no_lead", await withoutLead.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.Created, withLead.StatusCode);
    }

    [Fact]
    public async Task Algorithm_required_defaults_from_the_project()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory, algorithmRequiredDefault: true);

        var defaulted = await scenario.CreateTaskAsync();
        var overridden = await scenario.CreateTaskAsync(fields: new { algorithmRequired = false });

        Assert.True(defaulted.GetProperty("algorithmRequired").GetBoolean());
        Assert.False(overridden.GetProperty("algorithmRequired").GetBoolean());
    }

    [Fact]
    public async Task AC_TASK_2_developers_get_404_for_tasks_outside_their_projects()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        using var outsider = await factory.SignedInClientAsync(SystemRoles.DeveloperId);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);

        var get = await outsider.GetAsync($"/api/v1/tasks/{Id(task)}");
        var comment = await outsider.PostAsync($"/api/v1/tasks/{Id(task)}/comments", new { body = "Hello" });

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal("task.not_found", await get.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.NotFound, comment.StatusCode);
    }

    [Fact]
    public async Task AC_TASK_3_developers_see_only_tasks_assigned_to_them()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var mine1 = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id, "Mine one");
        var mine2 = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id, "Mine two");
        var theirs = await scenario.CreateAssignedTaskAsync(scenario.Dev2Member.Id, "Theirs");
        var draft = await scenario.CreateTaskAsync("Unassigned draft");

        var devList = await scenario.Dev.GetJsonAsync<JsonElement>($"/api/v1/tasks?projectId={scenario.ProjectId}");
        var viewerList = await scenario.Viewer.GetJsonAsync<JsonElement>($"/api/v1/tasks?projectId={scenario.ProjectId}");

        Assert.Equal(
            new[] { Id(mine1), Id(mine2) }.Order(),
            devList.GetProperty("items").EnumerateArray().Select(Id).Order());
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Dev.GetAsync($"/api/v1/tasks/{Id(theirs)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Dev.GetAsync($"/api/v1/tasks/{Id(draft)}")).StatusCode);
        Assert.Equal(4, viewerList.GetProperty("totalCount").GetInt32()); // Viewer: tasks.view at Project scope
    }

    [Fact]
    public async Task AC_TASK_4_the_second_of_two_concurrent_edits_gets_409_and_the_first_is_kept()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateTaskAsync();

        var first = await scenario.Pm.PatchAsync($"/api/v1/tasks/{Id(task)}", Edit(task, "Edited by PM"));
        var second = await scenario.Tl.PatchAsync($"/api/v1/tasks/{Id(task)}", Edit(task, "Edited by TL"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("concurrency_conflict", await second.ProblemCodeAsync());
        var current = await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/tasks/{Id(task)}");
        Assert.Equal("Edited by PM", current.GetProperty("title").GetString());
        Assert.Equal(240, current.GetProperty("allocatedMinutes").GetInt32());
        Assert.Equal("backend", Assert.Single(current.GetProperty("labels").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task Assigning_moves_the_task_on_records_history_and_cannot_be_repeated()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateTaskAsync(fields: new { algorithmRequired = true });
        var assignPath = $"/api/v1/tasks/{Id(task)}/assign";

        var toViewer = await scenario.Pm.PostAsync(assignPath, new { assigneeId = scenario.ViewerMember.Id });
        var toOutsider = await scenario.Pm.PostAsync(assignPath, new { assigneeId = Guid.NewGuid() });
        var byDeveloper = await scenario.Dev.PostAsync(assignPath, new { assigneeId = scenario.DevMember.Id });
        var assigned = await scenario.Tl.PostAsync(assignPath, new { assigneeId = scenario.DevMember.Id });
        var again = await scenario.Pm.PostAsync(assignPath, new { assigneeId = scenario.Dev2Member.Id });
        var history = await scenario.Dev.GetJsonAsync<JsonElement>($"/api/v1/tasks/{Id(task)}/history");

        Assert.Equal(HttpStatusCode.BadRequest, toViewer.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, toOutsider.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, byDeveloper.StatusCode); // the developer cannot see the unassigned draft
        var body = await assigned.JsonAsync();
        Assert.Equal("AwaitingAlgorithm", body.GetProperty("status").GetString());
        Assert.Equal(scenario.DevMember.Id, body.GetProperty("assignee").GetProperty("id").GetGuid());
        Assert.Equal(body.GetProperty("dueDate").GetString(), body.GetProperty("originalDueDate").GetString());
        Assert.Equal("task.invalid_transition", await again.ProblemCodeAsync());
        Assert.Equal(
            ["status:create", "assignment:assign", "status:assign"],
            history.EnumerateArray().Select(e => $"{e.GetProperty("kind").GetString()}:{e.GetProperty("action").GetString()}"));
    }

    [Fact]
    public async Task Assigning_needs_a_due_date_which_can_be_supplied_with_the_assignment()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateTaskAsync(fields: new { dueDate = (string?)null });

        var withoutDate = await scenario.Pm.PostAsync($"/api/v1/tasks/{Id(task)}/assign", new { assigneeId = scenario.DevMember.Id });
        var withDate = await scenario.Pm.PostAsync($"/api/v1/tasks/{Id(task)}/assign", new { assigneeId = scenario.DevMember.Id, dueDate = "2027-02-01" });

        Assert.Equal("task.due_date_required", await withoutDate.ProblemCodeAsync());
        Assert.Equal("2027-02-01", (await withDate.JsonAsync()).GetProperty("dueDate").GetString());
    }

    [Fact]
    public async Task Due_date_is_locked_after_assignment_and_developers_cannot_edit_tasks()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);

        var byDeveloper = await scenario.Dev.PatchAsync($"/api/v1/tasks/{Id(task)}", Edit(task, "Dev edit"));
        var moveDueDate = await scenario.Pm.PatchAsync($"/api/v1/tasks/{Id(task)}", Edit(task, "Later", dueDate: "2030-01-01"));
        var flagOn = await scenario.Pm.PatchAsync($"/api/v1/tasks/{Id(task)}", Edit(task, "Needs design", algorithmRequired: true));

        Assert.Equal(HttpStatusCode.Forbidden, byDeveloper.StatusCode);
        Assert.Equal("task.due_date_locked", await moveDueDate.ProblemCodeAsync());
        Assert.Equal("AwaitingAlgorithm", (await flagOn.JsonAsync()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Cancel_needs_a_reason_and_makes_the_task_read_only()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);

        var noReason = await scenario.Pm.PostAsync($"/api/v1/tasks/{Id(task)}/cancel", new { reason = " " });
        var byDeveloper = await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/cancel", new { reason = "Not mine" });
        var cancelled = await scenario.Tl.PostAsync($"/api/v1/tasks/{Id(task)}/cancel", new { reason = "Requirement dropped" });
        var edit = await scenario.Pm.PatchAsync($"/api/v1/tasks/{Id(task)}", Edit(await cancelled.JsonAsync(), "After cancel"));
        var cancelAgain = await scenario.Pm.PostAsync($"/api/v1/tasks/{Id(task)}/cancel", new { reason = "Again" });

        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byDeveloper.StatusCode);
        var body = await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/tasks/{Id(task)}");
        Assert.Equal("Cancelled", body.GetProperty("status").GetString());
        Assert.Equal("Requirement dropped", body.GetProperty("cancelReason").GetString());
        Assert.Empty(body.GetProperty("allowedActions").EnumerateArray());
        Assert.Equal("task.read_only", await edit.ProblemCodeAsync());
        Assert.Equal("task.invalid_transition", await cancelAgain.ProblemCodeAsync());
    }

    [Fact]
    public async Task Only_untouched_drafts_can_be_deleted()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var untouched = await scenario.CreateTaskAsync("Untouched");
        var commented = await scenario.CreateTaskAsync("Commented");
        var assigned = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        await scenario.Pm.PostAsync($"/api/v1/tasks/{Id(commented)}/comments", new { body = "Keep this" });

        var deleted = await scenario.Pm.DeleteAsync($"/api/v1/tasks/{Id(untouched)}");
        var withComment = await scenario.Pm.DeleteAsync($"/api/v1/tasks/{Id(commented)}");
        var afterAssignment = await scenario.Pm.DeleteAsync($"/api/v1/tasks/{Id(assigned)}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Pm.GetAsync($"/api/v1/tasks/{Id(untouched)}")).StatusCode);
        Assert.Equal("task.has_history", await withComment.ProblemCodeAsync());
        Assert.Equal("task.has_history", await afterAssignment.ProblemCodeAsync());
    }

    [Fact]
    public async Task Allowed_actions_reflect_role_and_state()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var draft = await scenario.CreateTaskAsync();
        var assigned = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);

        string[] Actions(JsonElement task) => task.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()!).ToArray();

        Assert.Equal(
            ["update", "assign", "cancel", "delete", "comment", "attach", "manageChecklist", "adjustTime"],
            Actions(await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/tasks/{Id(draft)}")));
        Assert.Equal(
            ["update", "cancel", "comment", "attach", "manageChecklist", "adjustTime", "requestReassignment", "requestExtension"],
            Actions(await scenario.Tl.GetJsonAsync<JsonElement>($"/api/v1/tasks/{Id(assigned)}")));
        Assert.Equal(
            ["comment", "attach", "start", "completeChecklist", "requestExtension", "reportDelay"],
            Actions(await scenario.Dev.GetJsonAsync<JsonElement>($"/api/v1/tasks/{Id(assigned)}")));
        Assert.Empty(Actions(await scenario.Viewer.GetJsonAsync<JsonElement>($"/api/v1/tasks/{Id(assigned)}")));
    }

    [Fact]
    public async Task AC_ROLE_1_removing_tasks_create_from_a_project_role_takes_effect_immediately()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);
        var role = await (await admin.PostAsync("/api/v1/roles", new { name = "Lead " + scenario.Key, description = "Test role" })).JsonAsync();
        var lead = await factory.CreateMemberAsync(SystemRoles.DeveloperId);
        using var leadClient = factory.CreateTestClient();
        await leadClient.SignInAsync(lead.Email, lead.Password);
        await scenario.AddMemberAsync(lead.Id, Id(role));
        var grant = await admin.PutAsync($"/api/v1/roles/{Id(role)}/permissions", new
        {
            permissions = new[] { new { code = "projects.view", scope = "Project" }, new { code = "tasks.view", scope = "Project" }, new { code = "tasks.create", scope = "Project" } },
            version = role.GetProperty("version").GetUInt32(),
        });
        var before = await leadClient.PostAsync($"/api/v1/projects/{scenario.ProjectId}/tasks", new { title = "Allowed" });

        await admin.PutAsync($"/api/v1/roles/{Id(role)}/permissions", new
        {
            permissions = new[] { new { code = "projects.view", scope = "Project" }, new { code = "tasks.view", scope = "Project" } },
            version = (await grant.JsonAsync()).GetProperty("version").GetUInt32(),
        });
        var after = await leadClient.PostAsync($"/api/v1/projects/{scenario.ProjectId}/tasks", new { title = "Refused" });

        Assert.Equal(HttpStatusCode.Created, before.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
    }

    [Fact]
    public async Task List_filters_searches_sorts_and_paginates_on_the_server()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)).ToString("yyyy-MM-dd");
        var critical = await scenario.CreateTaskAsync("Fix payment crash", new { priority = "Critical", type = "Bug" });
        var overdue = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id, "Overdue report", new { dueDate = yesterday, priority = "Low" });
        await scenario.CreateTaskAsync("Write docs", new { priority = "High" });
        await factory.Services.GetRequiredService<TaskManagement.Infrastructure.Jobs.DueDateWorker>().RunOnceAsync(TestContext.Current.CancellationToken);
        var basePath = $"/api/v1/tasks?projectId={scenario.ProjectId}";

        var byPriority = await scenario.Pm.GetJsonAsync<JsonElement>($"{basePath}&sort=-priority&pageSize=2");
        var drafts = await scenario.Pm.GetJsonAsync<JsonElement>($"{basePath}&status=Draft&priority=Critical&priority=High");
        var overdueOnly = await scenario.Pm.GetJsonAsync<JsonElement>($"{basePath}&overdue=true");
        var byTitle = await scenario.Pm.GetJsonAsync<JsonElement>($"{basePath}&q=PAYMENT");
        var byKey = await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/tasks?q={overdue.GetProperty("key").GetString()}");
        var badSort = await scenario.Pm.GetAsync($"{basePath}&sort=password");

        Assert.Equal(3, byPriority.GetProperty("totalCount").GetInt32());
        Assert.Equal(["Critical", "High"], byPriority.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("priority").GetString()));
        Assert.Equal(2, drafts.GetProperty("totalCount").GetInt32());
        var late = Assert.Single(overdueOnly.GetProperty("items").EnumerateArray());
        Assert.True(late.GetProperty("overdue").GetBoolean());
        Assert.Equal("Test Developer", late.GetProperty("assignee").GetProperty("name").GetString());
        Assert.Equal(Id(critical), Id(Assert.Single(byTitle.GetProperty("items").EnumerateArray())));
        Assert.Equal(Id(overdue), Id(Assert.Single(byKey.GetProperty("items").EnumerateArray())));
        Assert.Equal(HttpStatusCode.BadRequest, badSort.StatusCode);
    }

    [Fact]
    public async Task Task_changes_are_audited()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);

        var log = await admin.GetJsonAsync<JsonElement>($"/api/v1/audit-logs?entityType=Task&entityId={Id(task)}");

        Assert.Equal(
            ["task.assigned", "task.created"],
            log.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("action").GetString()));
        Assert.All(log.GetProperty("items").EnumerateArray(), e => Assert.Equal(scenario.ProjectId, e.GetProperty("projectId").GetGuid()));
    }
}
