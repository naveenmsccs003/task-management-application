using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Time;
using TaskManagement.Infrastructure.Jobs;
using TaskManagement.IntegrationTests.Infrastructure;
using TaskManagement.IntegrationTests.Projects;

namespace TaskManagement.IntegrationTests.Reports;

[Collection(ApiCollection.Name)]
public sealed class DashboardTests(ApiFactory factory)
{
    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    private static string Day(int offsetFromToday) => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(offsetFromToday)).ToString("yyyy-MM-dd");

    private static List<Guid> Ids(JsonElement list) => [.. list.GetProperty("items").EnumerateArray().Select(Id)];

    private static bool Has(JsonElement dashboard, string section) => dashboard.GetProperty(section).ValueKind != JsonValueKind.Null;

    private Task AddSessionAsync(Guid taskId, Guid userId, DateTimeOffset start, DateTimeOffset end) =>
        factory.WithDbAsync(async db =>
        {
            var session = WorkSession.Start(taskId, userId, 1, start);
            session.End(end, SessionEndReason.Paused);
            db.WorkSessions.Add(session);
            return await db.SaveChangesAsync();
        });

    private Task<int> RunDueDatesAsync() =>
        factory.Services.GetRequiredService<DueDateWorker>().RunOnceAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task FR_RPT_01_a_developer_sees_their_own_work()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var overdue = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id, "Late", new { dueDate = Day(-2) });
        var dueSoon = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id, "Soon", new { dueDate = Day(1) });
        await scenario.CreateAssignedTaskAsync(scenario.Dev2Member.Id, "Someone else's", new { dueDate = Day(-2) });
        await RunDueDatesAsync();
        var now = DateTimeOffset.UtcNow;
        await AddSessionAsync(Id(dueSoon), scenario.DevMember.Id, now.AddMinutes(-50), now.AddMinutes(-20));
        Assert.Equal(HttpStatusCode.OK, (await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(overdue)}/start")).StatusCode);

        var dashboard = await scenario.Dev.GetJsonAsync<JsonElement>("/api/v1/dashboard");

        var myWork = dashboard.GetProperty("myWork");
        Assert.Equal([Id(overdue)], Ids(myWork.GetProperty("overdue")));
        Assert.Equal([Id(dueSoon)], Ids(myWork.GetProperty("dueSoon")));
        Assert.Equal(Id(overdue), myWork.GetProperty("activeSession").GetProperty("taskId").GetGuid());
        Assert.InRange(myWork.GetProperty("secondsThisWeek").GetInt64(), 30 * 60, 32 * 60);
        var byStatus = myWork.GetProperty("byStatus").EnumerateArray().ToDictionary(s => s.GetProperty("status").GetString()!, s => s.GetProperty("count").GetInt32());
        Assert.Equal(new Dictionary<string, int> { ["ReadyToStart"] = 1, ["InProgress"] = 1 }, byStatus);
        Assert.False(Has(dashboard, "team"));
        Assert.False(Has(dashboard, "projects")); // Own scope of dashboard.view: no project overview
        Assert.False(Has(dashboard, "admin"));
    }

    [Fact]
    public async Task FR_RPT_02_a_team_leader_sees_what_waits_for_them()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var algorithm = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id, "Needs a plan", new { algorithmRequired = true });
        Assert.Equal(HttpStatusCode.OK, (await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(algorithm)}/algorithm", new { content = "Plan" })).StatusCode);
        var over = await scenario.CreateAssignedTaskAsync(scenario.Dev2Member.Id, "Over budget", new { allocatedMinutes = 60, dueDate = Day(-2) });
        await AddSessionAsync(Id(over), scenario.Dev2Member.Id, DateTimeOffset.UtcNow.AddMinutes(-100), DateTimeOffset.UtcNow.AddMinutes(-10));
        await RunDueDatesAsync();
        Assert.Equal(HttpStatusCode.OK, (await scenario.Dev2.PostAsync($"/api/v1/tasks/{Id(over)}/extension-requests", new { proposedDueDate = Day(5), reason = "Bigger than planned" })).StatusCode);

        var dashboard = await scenario.Tl.GetJsonAsync<JsonElement>("/api/v1/dashboard");

        var team = dashboard.GetProperty("team");
        Assert.Equal([Id(algorithm)], Ids(team.GetProperty("algorithmApprovals")));
        Assert.Equal(0, team.GetProperty("reviews").GetProperty("total").GetInt32());
        var overAllocated = Assert.Single(team.GetProperty("overAllocated").EnumerateArray());
        Assert.Equal(Id(over), Id(overAllocated));
        Assert.Equal(3600, overAllocated.GetProperty("allocatedSeconds").GetInt64());
        Assert.Equal(5400, overAllocated.GetProperty("spentSeconds").GetInt64());
        Assert.Equal([Id(over)], Ids(team.GetProperty("overdue")));
        Assert.Equal(1, team.GetProperty("pendingRequests").GetInt32());
        var workload = team.GetProperty("workload").EnumerateArray()
            .ToDictionary(w => w.GetProperty("member").GetProperty("id").GetGuid(), w => (Open: w.GetProperty("openTasks").GetInt32(), Seconds: w.GetProperty("secondsThisWeek").GetInt64()));
        Assert.Equal(1, workload[scenario.DevMember.Id].Open);
        Assert.Equal((1, 5400L), workload[scenario.Dev2Member.Id]);
        Assert.True(Has(dashboard, "projects"));
        Assert.False(Has(dashboard, "admin"));

        // The PM leads no reviews or algorithms: no Team section, but the project overview.
        var pm = await scenario.Pm.GetJsonAsync<JsonElement>("/api/v1/dashboard");
        Assert.False(Has(pm, "team"));
        Assert.False(Has(pm, "myWork"));
    }

    [Fact]
    public async Task FR_RPT_03_project_progress_counts_tasks_time_overdue_extensions_and_revisions()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var late = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id, "Late", new { dueDate = Day(-2), allocatedMinutes = 120 });
        await scenario.CreateTaskAsync("Draft", new { allocatedMinutes = 30 });
        await AddSessionAsync(Id(late), scenario.DevMember.Id, DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-1));
        await RunDueDatesAsync();
        var path = $"/api/v1/tasks/{Id(late)}";
        await scenario.Dev.PostAsync($"{path}/start");
        await scenario.Dev.PostAsync($"{path}/submit-review", new { summary = "Done" });
        await scenario.Tl.PostAsync($"{path}/request-revision", new { comment = "Again", algorithmReapprovalRequired = false });
        await scenario.Dev.PostAsync($"{path}/extension-requests", new { proposedDueDate = Day(3), reason = "Revision takes longer" });
        var requestId = Id((await scenario.Tl.GetJsonAsync<JsonElement>($"{path}/requests"))[0]);
        Assert.Equal(HttpStatusCode.OK, (await scenario.Pm.PostAsync($"/api/v1/requests/{requestId}/approve")).StatusCode);
        await scenario.CreateAssignedTaskAsync(scenario.Dev2Member.Id, "Still late", new { dueDate = Day(-2) });
        await RunDueDatesAsync();

        var dashboard = await scenario.Pm.GetJsonAsync<JsonElement>("/api/v1/dashboard");

        var project = dashboard.GetProperty("projects").GetProperty("items").EnumerateArray().Single(p => Id(p) == scenario.ProjectId);
        var byStatus = project.GetProperty("byStatus").EnumerateArray().ToDictionary(s => s.GetProperty("status").GetString()!, s => s.GetProperty("count").GetInt32());
        Assert.Equal(new Dictionary<string, int> { ["Draft"] = 1, ["ReadyToStart"] = 1, ["RevisionRequested"] = 1 }, byStatus);
        Assert.Equal(150 * 60, project.GetProperty("allocatedSeconds").GetInt64());
        Assert.InRange(project.GetProperty("spentSeconds").GetInt64(), 3600, 3660);
        Assert.Equal(1, project.GetProperty("overdue").GetInt32()); // the extension cleared the first task's flag
        Assert.Equal(1, project.GetProperty("extensionsApproved").GetInt32());
        Assert.Equal(1, project.GetProperty("revisionsRequested").GetInt32());
        var trend = project.GetProperty("overdueTrend").EnumerateArray().ToList();
        Assert.Equal(8, trend.Count);
        Assert.Equal(1, trend[^1].GetProperty("count").GetInt32());

        // A viewer of the project sees the same overview and nothing else.
        var viewer = await scenario.Viewer.GetJsonAsync<JsonElement>("/api/v1/dashboard");
        Assert.Contains(viewer.GetProperty("projects").GetProperty("items").EnumerateArray(), p => Id(p) == scenario.ProjectId);
        Assert.False(Has(viewer, "team"));
        Assert.False(Has(viewer, "myWork"));
    }

    [Fact]
    public async Task FR_RPT_04_admins_see_members_sign_in_failures_integrations_and_jobs()
    {
        var member = await factory.CreateMemberAsync(SystemRoles.DeveloperId);
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);
        var before = (await admin.GetJsonAsync<JsonElement>("/api/v1/dashboard")).GetProperty("admin").GetProperty("failedSignIns24Hours").GetInt32();
        using (var anonymous = factory.CreateTestClient())
        {
            await anonymous.SignInAsync(member.Email, "not-the-password", ensureSuccess: false);
        }

        var dashboard = await admin.GetJsonAsync<JsonElement>("/api/v1/dashboard");

        var section = dashboard.GetProperty("admin");
        Assert.True(section.GetProperty("failedSignIns24Hours").GetInt32() > before);
        Assert.True(section.GetProperty("activeMembers").GetInt32() >= 2);
        Assert.Equal(JsonValueKind.Array, section.GetProperty("gitLab").ValueKind);
        Assert.Equal(JsonValueKind.Array, section.GetProperty("jobs").ValueKind);
        Assert.False(Has(dashboard, "myWork"));
        Assert.True(Has(dashboard, "projects")); // Global dashboard.view: every active project

        using var developer = await factory.SignedInClientAsync(SystemRoles.DeveloperId);
        Assert.False(Has(await developer.GetJsonAsync<JsonElement>("/api/v1/dashboard"), "admin"));
    }
}
