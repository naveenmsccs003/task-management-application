using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Notifications;
using TaskManagement.Domain.Tasks;
using TaskManagement.Domain.Time;
using TaskManagement.IntegrationTests.Infrastructure;
using TaskManagement.IntegrationTests.Projects;

namespace TaskManagement.IntegrationTests.Review;

[Collection(ApiCollection.Name)]
public sealed class RequestTests(ApiFactory factory)
{
    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    private static string Day(int offsetFromToday) => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(offsetFromToday)).ToString("yyyy-MM-dd");

    private static async Task<Guid> PendingRequestIdAsync(TestClient client, Guid taskId) =>
        Id((await client.GetJsonAsync<JsonElement>($"/api/v1/tasks/{taskId}/requests")).EnumerateArray().First(r => r.GetProperty("status").GetString() == "Pending"));

    [Fact]
    public async Task Reassignment_by_a_team_leader_is_approved_by_a_project_manager_and_moves_the_work()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/start");

        var shortReason = await scenario.Tl.PostAsync($"/api/v1/tasks/{Id(task)}/reassignment-requests", new { newAssigneeId = scenario.Dev2Member.Id, reason = "Leave" });
        var requested = await scenario.Tl.PostAsync($"/api/v1/tasks/{Id(task)}/reassignment-requests", new { newAssigneeId = scenario.Dev2Member.Id, reason = "Dana is on leave next week." });
        var duplicate = await scenario.Pm.PostAsync($"/api/v1/tasks/{Id(task)}/reassignment-requests", new { newAssigneeId = scenario.Dev2Member.Id, reason = "Second request for the same task." });
        var requestId = await PendingRequestIdAsync(scenario.Tl, Id(task));
        var byRequester = await scenario.Tl.PostAsync($"/api/v1/requests/{requestId}/approve");
        var queue = await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/requests?projectId={scenario.ProjectId}");
        var approved = await scenario.Pm.PostAsync($"/api/v1/requests/{requestId}/approve", new { comment = "OK" });

        Assert.Equal(HttpStatusCode.BadRequest, shortReason.StatusCode);
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        Assert.Equal("request.pending_exists", await duplicate.ProblemCodeAsync());
        Assert.Equal("request.own_request", await byRequester.ProblemCodeAsync());
        Assert.Contains(queue.EnumerateArray(), r => Id(r) == requestId && r.GetProperty("canDecide").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var view = await scenario.Dev2.GetJsonAsync<JsonElement>($"/api/v1/tasks/{Id(task)}");
        Assert.Equal(scenario.Dev2Member.Id, view.GetProperty("assignee").GetProperty("id").GetGuid());
        Assert.Equal("Stopped", view.GetProperty("status").GetString()); // work had started
        var session = await factory.WithDbAsync(db => db.WorkSessions.AsNoTracking().SingleAsync(s => s.TaskId == Id(task)));
        Assert.Equal(SessionEndReason.Reassigned, session.EndReason);
        Assert.Equal(HttpStatusCode.OK, (await scenario.Dev.GetAsync($"/api/v1/tasks/{Id(task)}")).StatusCode); // previously assigned
        var history = await scenario.Dev2.GetJsonAsync<JsonElement>($"/api/v1/tasks/{Id(task)}/history");
        Assert.Contains(history.EnumerateArray(), e => e.GetProperty("action").GetString() == "reassign" && e.GetProperty("kind").GetString() == "assignment");
        Assert.True(await factory.WithDbAsync(db => db.Notifications.AnyAsync(n => n.RecipientId == scenario.Dev2Member.Id && n.Type == NotificationType.TaskReassigned)));
    }

    [Fact]
    public async Task A_team_leader_cannot_approve_another_team_leaders_request_but_an_admin_can()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);
        var otherLead = await factory.CreateMemberAsync(SystemRoles.TeamLeaderId);
        using var otherLeadClient = factory.CreateTestClient();
        await otherLeadClient.SignInAsync(otherLead.Email, otherLead.Password);
        await scenario.AddMemberAsync(otherLead.Id, SystemRoles.TeamLeaderId);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        await scenario.Tl.PostAsync($"/api/v1/tasks/{Id(task)}/reassignment-requests", new { newAssigneeId = scenario.Dev2Member.Id, reason = "Rebalancing the sprint." });
        var requestId = await PendingRequestIdAsync(scenario.Tl, Id(task));

        var bySameRole = await otherLeadClient.PostAsync($"/api/v1/requests/{requestId}/approve");
        var byAdmin = await admin.PostAsync($"/api/v1/requests/{requestId}/approve");

        Assert.Equal("request.same_role", await bySameRole.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.OK, byAdmin.StatusCode);
    }

    [Fact]
    public async Task Extension_requests_check_dates_and_can_be_approved_with_a_different_date()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        var due = DateOnly.Parse(task.GetProperty("dueDate").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        var path = $"/api/v1/tasks/{Id(task)}/extension-requests";

        var past = await scenario.Dev.PostAsync(path, new { proposedDueDate = Day(-1), reason = "Need more time to test." });
        var earlier = await scenario.Dev.PostAsync(path, new { proposedDueDate = due.AddDays(-1).ToString("yyyy-MM-dd"), reason = "Need more time to test." });
        var byOtherDeveloper = await scenario.Dev2.PostAsync(path, new { proposedDueDate = due.AddDays(7).ToString("yyyy-MM-dd"), reason = "Need more time to test." });
        var requested = await scenario.Dev.PostAsync(path, new { proposedDueDate = due.AddDays(7).ToString("yyyy-MM-dd"), reason = "Need more time to test." });
        var requestId = await PendingRequestIdAsync(scenario.Tl, Id(task));
        var pastApproval = await scenario.Tl.PostAsync($"/api/v1/requests/{requestId}/approve", new { approvedDueDate = Day(-2) });
        var approved = await scenario.Tl.PostAsync($"/api/v1/requests/{requestId}/approve", new { approvedDueDate = due.AddDays(3).ToString("yyyy-MM-dd") });

        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, earlier.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, byOtherDeveloper.StatusCode);
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, pastApproval.StatusCode); // edge case 25
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var view = await scenario.Dev.GetJsonAsync<JsonElement>($"/api/v1/tasks/{Id(task)}");
        Assert.Equal(due.AddDays(3).ToString("yyyy-MM-dd"), view.GetProperty("dueDate").GetString());
        Assert.Equal(due.ToString("yyyy-MM-dd"), view.GetProperty("originalDueDate").GetString());
        Assert.False(view.GetProperty("flags").GetProperty("extensionPending").GetBoolean());
    }

    [Fact]
    public async Task A_project_managers_extension_is_decided_by_a_team_leader_and_requests_can_be_withdrawn()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        var due = DateOnly.Parse(task.GetProperty("dueDate").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        await scenario.Pm.PostAsync($"/api/v1/tasks/{Id(task)}/extension-requests", new { proposedDueDate = due.AddDays(5).ToString("yyyy-MM-dd"), reason = "Client moved the launch." });
        var requestId = await PendingRequestIdAsync(scenario.Pm, Id(task));

        var withdrawByOther = await scenario.Tl.PostAsync($"/api/v1/requests/{requestId}/withdraw");
        var rejectWithoutComment = await scenario.Tl.PostAsync($"/api/v1/requests/{requestId}/reject", new { comment = "" });
        var withdrawn = await scenario.Pm.PostAsync($"/api/v1/requests/{requestId}/withdraw");

        Assert.Equal("request.not_requester", await withdrawByOther.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, rejectWithoutComment.StatusCode);
        Assert.Equal("Withdrawn", (await withdrawn.JsonAsync())[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Edge_cases_26_and_27_pending_requests_end_with_the_task_or_project()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var cancelled = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id, "To cancel");
        var archived = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id, "Project archived");
        await scenario.Tl.PostAsync($"/api/v1/tasks/{Id(cancelled)}/reassignment-requests", new { newAssigneeId = scenario.Dev2Member.Id, reason = "Moving this to Olly." });
        await scenario.Tl.PostAsync($"/api/v1/tasks/{Id(archived)}/reassignment-requests", new { newAssigneeId = scenario.Dev2Member.Id, reason = "Moving this to Olly." });

        await scenario.Pm.PostAsync($"/api/v1/tasks/{Id(cancelled)}/cancel", new { reason = "Duplicate" });
        await scenario.Pm.PostAsync($"/api/v1/projects/{scenario.ProjectId}/archive", new { confirmOpenTasks = true });

        var statuses = await factory.WithDbAsync(db => db.ApprovalRequests.AsNoTracking()
            .Where(r => r.TaskId == Id(cancelled) || r.TaskId == Id(archived)).Select(r => r.Status).ToListAsync());
        Assert.Equal([ApprovalRequestStatus.AutoCancelled, ApprovalRequestStatus.AutoCancelled], statuses);
    }

    [Fact]
    public async Task Review_is_blocked_while_a_reassignment_is_pending()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/start");
        await scenario.Tl.PostAsync($"/api/v1/tasks/{Id(task)}/reassignment-requests", new { newAssigneeId = scenario.Dev2Member.Id, reason = "Rebalancing the sprint." });

        var response = await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/submit-review", new { summary = "Done" });

        Assert.Equal("review.reassignment_pending", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Reassigning_while_an_algorithm_waits_withdraws_it_so_the_new_assignee_can_submit()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory, algorithmRequiredDefault: true);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/algorithm", new { content = "Old plan" });
        await scenario.Tl.PostAsync($"/api/v1/tasks/{Id(task)}/reassignment-requests", new { newAssigneeId = scenario.Dev2Member.Id, reason = "Dana moved to another team." });
        await scenario.Pm.PostAsync($"/api/v1/requests/{await PendingRequestIdAsync(scenario.Pm, Id(task))}/approve");

        var view = await scenario.Dev2.GetJsonAsync<JsonElement>($"/api/v1/tasks/{Id(task)}");
        var submitted = await scenario.Dev2.PostAsync($"/api/v1/tasks/{Id(task)}/algorithm", new { content = "New plan" });

        Assert.Equal("AwaitingAlgorithm", view.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var decisions = await factory.WithDbAsync(db => db.AlgorithmSubmissions.AsNoTracking().Where(a => a.TaskId == Id(task)).OrderBy(a => a.Version).Select(a => a.Decision).ToListAsync());
        Assert.Equal([AlgorithmDecision.Withdrawn, AlgorithmDecision.Pending], decisions);
    }
}
