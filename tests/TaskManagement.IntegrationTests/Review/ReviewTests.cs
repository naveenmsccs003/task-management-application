using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaskManagement.Domain.Notifications;
using TaskManagement.Domain.Time;
using TaskManagement.IntegrationTests.Infrastructure;
using TaskManagement.IntegrationTests.Projects;

namespace TaskManagement.IntegrationTests.Review;

[Collection(ApiCollection.Name)]
public sealed class ReviewTests(ApiFactory factory)
{
    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    private static string Status(JsonElement task) => task.GetProperty("status").GetString()!;

    /// <summary>An assigned task with one required checklist item, already started by the developer.</summary>
    private static async Task<(JsonElement Task, Guid ItemId)> StartedTaskWithChecklistAsync(ProjectScenario scenario)
    {
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        var items = await (await scenario.Tl.PostAsync($"/api/v1/tasks/{Id(task)}/checklist", new { text = "Tests pass", isMandatory = true })).JsonAsync();
        Assert.Equal(HttpStatusCode.OK, (await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/start")).StatusCode);
        return (task, Id(items[0]));
    }

    [Fact]
    public async Task Full_review_cycle_with_a_revision_then_approval_closure_and_reopen()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var (task, itemId) = await StartedTaskWithChecklistAsync(scenario);
        var path = $"/api/v1/tasks/{Id(task)}";

        // AC-CHK-1: an unticked required item blocks submission and is named.
        var blocked = await scenario.Dev.PostAsync($"{path}/submit-review", new { summary = "Done" });
        var problem = await blocked.JsonAsync();
        Assert.Equal("review.checklist_incomplete", problem.GetProperty("code").GetString());
        Assert.Contains("Tests pass", problem.GetProperty("title").GetString(), StringComparison.Ordinal);

        await scenario.Dev.PostAsync($"{path}/checklist/{itemId}/complete");
        var submitted = await (await scenario.Dev.PostAsync($"{path}/submit-review", new { summary = "Login form built." })).JsonAsync();
        Assert.Equal("InReview", Status(submitted));
        var sessions = await factory.WithDbAsync(db => db.WorkSessions.AsNoTracking().Where(s => s.TaskId == Id(task)).ToListAsync());
        Assert.Equal(SessionEndReason.Submitted, Assert.Single(sessions).EndReason);

        // AC-RVW-1: a revision request needs a comment. AC-RVW-2: the revision goes up and history stays.
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Tl.PostAsync($"{path}/request-revision", new { comment = "" })).StatusCode);
        var revised = await (await scenario.Tl.PostAsync($"{path}/request-revision", new
        {
            comment = "Handle the empty email case.",
            reopenChecklistItemIds = new[] { itemId },
            algorithmReapprovalRequired = false,
        })).JsonAsync();
        Assert.Equal("RevisionRequested", Status(revised));
        Assert.Equal(1, revised.GetProperty("currentRevision").GetInt32());
        Assert.Equal(0, revised.GetProperty("checklist").GetProperty("mandatoryDone").GetInt32());
        var reviews = await scenario.Dev.GetJsonAsync<JsonElement>($"{path}/reviews");
        Assert.Equal("RevisionRequested", Assert.Single(reviews.EnumerateArray()).GetProperty("decision").GetString());

        // Second round: approve, then close (AC-RVW-4: closed tasks refuse edits), then reopen.
        await scenario.Dev.PostAsync($"{path}/start");
        await scenario.Dev.PostAsync($"{path}/checklist/{itemId}/complete");
        await scenario.Dev.PostAsync($"{path}/submit-review", new { summary = "Empty email handled." });
        var approved = await (await scenario.Tl.PostAsync($"{path}/approve", new { comment = "Good." })).JsonAsync();
        Assert.Equal("Approved", Status(approved));
        Assert.Equal(1, approved.GetProperty("approvedRevision").GetInt32());
        Assert.Contains("close", approved.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()));
        var closed = await (await scenario.Pm.PostAsync($"{path}/close")).JsonAsync();
        Assert.Equal("Closed", Status(closed));
        var edit = await scenario.Pm.PatchAsync(path, new { title = "Changed", description = "", dueDate = closed.GetProperty("dueDate").GetString(), labels = Array.Empty<string>(), version = closed.GetProperty("version").GetUInt32() });
        Assert.Equal("task.read_only", await edit.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Pm.PostAsync($"{path}/reopen", new { reason = " " })).StatusCode);
        var reopened = await (await scenario.Pm.PostAsync($"{path}/reopen", new { reason = "Customer found a bug." })).JsonAsync();
        Assert.Equal("RevisionRequested", Status(reopened));
        Assert.Equal(2, reopened.GetProperty("currentRevision").GetInt32());
    }

    [Fact]
    public async Task AC_RVW_3_a_task_that_is_not_approved_cannot_be_closed()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/start");
        await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/submit-review", new { summary = "Done" });

        var response = await scenario.Pm.PostAsync($"/api/v1/tasks/{Id(task)}/close");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("task.invalid_transition", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task FR_RVW_03_a_team_leader_cannot_review_their_own_task_and_developers_cannot_review()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.TlMember.Id);
        await scenario.Tl.PostAsync($"/api/v1/tasks/{Id(task)}/start");
        await scenario.Tl.PostAsync($"/api/v1/tasks/{Id(task)}/submit-review", new { summary = "My own work" });

        var own = await scenario.Tl.PostAsync($"/api/v1/tasks/{Id(task)}/approve");
        var byManager = await scenario.Pm.PostAsync($"/api/v1/tasks/{Id(task)}/approve");

        Assert.Equal("review.own_work", await own.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, byManager.StatusCode); // tasks.review is TL-only by default
    }

    [Fact]
    public async Task Close_on_approval_closes_the_task_with_the_approval()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var project = await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/projects/{scenario.ProjectId}");
        await scenario.Pm.PatchAsync($"/api/v1/projects/{scenario.ProjectId}", new
        {
            name = project.GetProperty("name").GetString(),
            description = "",
            algorithmRequiredDefault = false,
            closeOnApproval = true,
            version = project.GetProperty("version").GetUInt32(),
        });
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/start");
        await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/submit-review", new { summary = "Done" });

        var approved = await (await scenario.Tl.PostAsync($"/api/v1/tasks/{Id(task)}/approve")).JsonAsync();

        Assert.Equal("Closed", Status(approved));
    }

    [Fact]
    public async Task FR_ALG_06_a_revision_can_require_the_algorithm_to_be_approved_again()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory, algorithmRequiredDefault: true);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        var path = $"/api/v1/tasks/{Id(task)}";
        await scenario.Dev.PostAsync($"{path}/algorithm", new { content = "Plan" });
        var submission = Id((await scenario.Tl.GetJsonAsync<JsonElement>($"{path}/algorithm"))[0]);
        await scenario.Tl.PostAsync($"{path}/algorithm/{submission}/approve");
        await scenario.Dev.PostAsync($"{path}/start");
        await scenario.Dev.PostAsync($"{path}/submit-review", new { summary = "Done" });

        var revised = await (await scenario.Tl.PostAsync($"{path}/request-revision", new { comment = "Different approach needed.", algorithmReapprovalRequired = true })).JsonAsync();
        var start = await scenario.Dev.PostAsync($"{path}/start");

        Assert.Equal("AwaitingAlgorithm", Status(revised));
        Assert.False(revised.GetProperty("algorithmApproved").GetBoolean());
        Assert.Equal("task.algorithm_approval_required", await start.ProblemCodeAsync());
    }

    [Fact]
    public async Task Review_events_notify_the_team_leader_and_the_assignee()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/start");
        await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/submit-review", new { summary = "Done" });
        await scenario.Tl.PostAsync($"/api/v1/tasks/{Id(task)}/request-revision", new { comment = "Add tests please." });

        var types = await factory.WithDbAsync(db => db.Notifications.AsNoTracking()
            .Where(n => n.TaskId == Id(task)).Select(n => new { n.RecipientId, n.Type }).ToListAsync());

        Assert.Contains(types, n => n.RecipientId == scenario.TlMember.Id && n.Type == NotificationType.SubmittedForReview);
        Assert.Contains(types, n => n.RecipientId == scenario.DevMember.Id && n.Type == NotificationType.RevisionRequested);
    }
}
