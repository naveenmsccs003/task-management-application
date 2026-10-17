using System.Net;
using System.Text.Json;
using TaskManagement.Domain.Access;
using TaskManagement.IntegrationTests.Infrastructure;

namespace TaskManagement.IntegrationTests.Projects;

[Collection(ApiCollection.Name)]
public sealed class ProjectTests(ApiFactory factory)
{
    private static object NewProject(string? key = null) => new
    {
        key = key ?? ProjectScenario.NewKey(),
        name = "Website relaunch",
        description = "Marketing site",
        startDate = "2026-11-01",
        targetEndDate = "2027-01-31",
        algorithmRequiredDefault = true,
        closeOnApproval = false,
    };

    [Fact]
    public async Task Project_manager_creates_a_project_and_becomes_its_project_manager()
    {
        using var pm = await factory.SignedInClientAsync(SystemRoles.ProjectManagerId);

        var response = await pm.PostAsync("/api/v1/projects", NewProject());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var project = await response.JsonAsync();
        Assert.Equal("Active", project.GetProperty("status").GetString());
        Assert.Equal("Project Manager", project.GetProperty("myRole").GetProperty("name").GetString());
        Assert.Equal(1, project.GetProperty("memberCount").GetInt32());
        Assert.Contains(project.GetProperty("myPermissions").EnumerateArray(), p => p.GetProperty("code").GetString() == "tasks.create");
    }

    [Fact]
    public async Task Admin_creates_a_project_without_joining_it()
    {
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);

        var project = await (await admin.PostAsync("/api/v1/projects", NewProject())).JsonAsync();

        Assert.Equal(JsonValueKind.Null, project.GetProperty("myRole").ValueKind);
        Assert.Equal(0, project.GetProperty("memberCount").GetInt32());
    }

    [Theory]
    [InlineData("Team Leader")]
    [InlineData("Developer")]
    [InlineData("Viewer")]
    public async Task Members_without_projects_create_cannot_create_projects(string roleName)
    {
        using var client = await factory.SignedInClientAsync(SystemRoles.All.Single(r => r.Name == roleName).Id);

        var response = await client.PostAsync("/api/v1/projects", NewProject());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Keys_are_unique_upper_cased_and_validated()
    {
        using var pm = await factory.SignedInClientAsync(SystemRoles.ProjectManagerId);
        var key = ProjectScenario.NewKey();

        var lower = await pm.PostAsync("/api/v1/projects", NewProject(key.ToLowerInvariant()));
        var duplicate = await pm.PostAsync("/api/v1/projects", NewProject(key));
        var invalid = await pm.PostAsync("/api/v1/projects", NewProject("1-BAD"));

        Assert.Equal(key, (await lower.JsonAsync()).GetProperty("key").GetString());
        Assert.Equal("project.key_taken", await duplicate.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.True((await invalid.JsonAsync()).GetProperty("errors").TryGetProperty("key", out _));
    }

    [Fact]
    public async Task FR_PROJ_03_members_see_only_their_projects()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        using var outsider = await factory.SignedInClientAsync(SystemRoles.DeveloperId);
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);

        var outsiderGet = await outsider.GetAsync($"/api/v1/projects/{scenario.ProjectId}");
        var outsiderList = await outsider.GetJsonAsync<JsonElement>($"/api/v1/projects?search={scenario.Key}");
        var memberList = await scenario.Dev.GetJsonAsync<JsonElement>($"/api/v1/projects?search={scenario.Key}");
        var adminGet = await admin.GetAsync($"/api/v1/projects/{scenario.ProjectId}");

        Assert.Equal(HttpStatusCode.NotFound, outsiderGet.StatusCode);
        Assert.Equal(0, outsiderList.GetProperty("totalCount").GetInt32());
        Assert.Equal("Developer", Assert.Single(memberList.GetProperty("items").EnumerateArray()).GetProperty("myRole").GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.OK, adminGet.StatusCode);
    }

    [Fact]
    public async Task Only_project_managers_can_edit_and_stale_versions_are_rejected()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var project = await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/projects/{scenario.ProjectId}");
        object Edit(string name) => new
        {
            name,
            description = "Updated",
            algorithmRequiredDefault = false,
            closeOnApproval = true,
            version = project.GetProperty("version").GetUInt32(),
        };

        var byDeveloper = await scenario.Dev.PatchAsync($"/api/v1/projects/{scenario.ProjectId}", Edit("Dev edit"));
        var first = await scenario.Pm.PatchAsync($"/api/v1/projects/{scenario.ProjectId}", Edit("First"));
        var second = await scenario.Pm.PatchAsync($"/api/v1/projects/{scenario.ProjectId}", Edit("Second"));

        Assert.Equal(HttpStatusCode.Forbidden, byDeveloper.StatusCode);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.True((await first.JsonAsync()).GetProperty("closeOnApproval").GetBoolean());
        Assert.Equal("concurrency_conflict", await second.ProblemCodeAsync());
    }

    [Fact]
    public async Task Creating_a_task_does_not_change_the_project_version()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var before = (await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/projects/{scenario.ProjectId}")).GetProperty("version").GetUInt32();

        await scenario.CreateTaskAsync();

        var after = (await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/projects/{scenario.ProjectId}")).GetProperty("version").GetUInt32();
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Project_members_are_added_with_a_project_role_and_self_changes_are_blocked()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var newcomer = await factory.CreateMemberAsync(SystemRoles.DeveloperId);
        var inactive = await factory.CreateMemberAsync(SystemRoles.DeveloperId);
        await factory.WithDbAsync(async db =>
        {
            var user = await db.Users.FindAsync([inactive.Id], TestContext.Current.CancellationToken);
            user!.Deactivate(scenario.PmMember.Id, DateTimeOffset.UtcNow);
            return await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        });
        var path = $"/api/v1/projects/{scenario.ProjectId}/members";

        var candidates = await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/projects/{scenario.ProjectId}/member-candidates?search={newcomer.Email}");
        var asAdminRole = await scenario.Pm.PostAsync(path, new { userId = newcomer.Id, projectRoleId = SystemRoles.AdminId });
        var added = await scenario.Pm.PostAsync(path, new { userId = newcomer.Id, projectRoleId = SystemRoles.DeveloperId });
        var again = await scenario.Pm.PostAsync(path, new { userId = newcomer.Id, projectRoleId = SystemRoles.DeveloperId });
        var deactivated = await scenario.Pm.PostAsync(path, new { userId = inactive.Id, projectRoleId = SystemRoles.DeveloperId });
        var self = await scenario.Pm.PatchAsync($"{path}/{scenario.PmMember.Id}", new { projectRoleId = SystemRoles.ViewerId });
        var byDeveloper = await scenario.Dev.PostAsync(path, new { userId = inactive.Id, projectRoleId = SystemRoles.DeveloperId });
        var promoted = await scenario.Pm.PatchAsync($"{path}/{newcomer.Id}", new { projectRoleId = SystemRoles.TeamLeaderId });

        Assert.Equal(newcomer.Id, Assert.Single(candidates.EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.BadRequest, asAdminRole.StatusCode);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Assert.Equal("project.already_member", await again.ProblemCodeAsync());
        Assert.Equal("member.inactive", await deactivated.ProblemCodeAsync());
        Assert.Equal("project.self_change", await self.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, byDeveloper.StatusCode);
        var members = await promoted.JsonAsync();
        Assert.Equal("Team Leader", members.EnumerateArray().Single(m => m.GetProperty("userId").GetGuid() == newcomer.Id).GetProperty("projectRole").GetProperty("name").GetString());
    }

    [Fact]
    public async Task FR_PROJ_05_removing_a_member_with_open_tasks_is_blocked_and_removal_ends_access()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        var taskId = task.GetProperty("id").GetGuid();
        var memberPath = $"/api/v1/projects/{scenario.ProjectId}/members/{scenario.DevMember.Id}";

        var blocked = await scenario.Pm.DeleteAsync(memberPath);
        var demoteBlocked = await scenario.Pm.PatchAsync(memberPath, new { projectRoleId = SystemRoles.ViewerId });
        Assert.Equal(HttpStatusCode.OK, (await scenario.Pm.PostAsync($"/api/v1/tasks/{taskId}/cancel", new { reason = "Scope cut" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await scenario.Dev.GetAsync($"/api/v1/tasks/{taskId}")).StatusCode);
        var removed = await scenario.Pm.DeleteAsync(memberPath);

        Assert.Equal("project.member_has_open_tasks", await blocked.ProblemCodeAsync());
        Assert.Equal("project.member_has_open_tasks", await demoteBlocked.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Dev.GetAsync($"/api/v1/tasks/{taskId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Dev.GetAsync($"/api/v1/projects/{scenario.ProjectId}")).StatusCode);
    }

    [Fact]
    public async Task FR_PROJ_04_completing_with_open_tasks_needs_confirmation_and_makes_tasks_read_only()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateTaskAsync();
        var taskId = task.GetProperty("id").GetGuid();
        var projectPath = $"/api/v1/projects/{scenario.ProjectId}";

        var unconfirmed = await scenario.Pm.PostAsync($"{projectPath}/complete");
        var confirmed = await scenario.Pm.PostAsync($"{projectPath}/complete", new { confirmOpenTasks = true });
        var edit = await scenario.Pm.PatchAsync($"/api/v1/tasks/{taskId}", new
        {
            title = "Changed",
            description = "",
            type = "Bug",
            priority = "Low",
            dueDate = task.GetProperty("dueDate").GetString(),
            labels = Array.Empty<string>(),
            version = task.GetProperty("version").GetUInt32(),
        });
        var newTask = await scenario.Pm.PostAsync($"{projectPath}/tasks", new { title = "Late addition" });
        var readOnlyTask = await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/tasks/{taskId}");
        var reactivated = await scenario.Pm.PostAsync($"{projectPath}/reactivate");

        Assert.Equal("project.has_open_tasks", await unconfirmed.ProblemCodeAsync());
        Assert.Equal("Completed", (await confirmed.JsonAsync()).GetProperty("status").GetString());
        Assert.Equal("project.read_only", await edit.ProblemCodeAsync());
        Assert.Equal("project.not_active", await newTask.ProblemCodeAsync());
        Assert.Equal("Draft", readOnlyTask.GetProperty("status").GetString());
        Assert.Empty(readOnlyTask.GetProperty("allowedActions").EnumerateArray());
        Assert.Equal("Active", (await reactivated.JsonAsync()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Project_changes_are_audited_with_the_project_id()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);

        var log = await admin.GetJsonAsync<JsonElement>($"/api/v1/audit-logs?entityType=ProjectMember&pageSize=100");

        var entries = log.GetProperty("items").EnumerateArray()
            .Where(e => e.GetProperty("projectId").ValueKind == JsonValueKind.String && e.GetProperty("projectId").GetGuid() == scenario.ProjectId)
            .ToList();
        Assert.Equal(4, entries.Count(e => e.GetProperty("action").GetString() == "project.member_added"));
    }
}
