using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Time;
using TaskManagement.Infrastructure.Jobs;
using TaskManagement.IntegrationTests.Infrastructure;
using TaskManagement.IntegrationTests.Projects;

namespace TaskManagement.IntegrationTests.Work;

[Collection(ApiCollection.Name)]
public sealed class WorkSessionTests(ApiFactory factory)
{
    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    private Task<List<WorkSession>> SessionsAsync(Guid taskId) =>
        factory.WithDbAsync(db => db.WorkSessions.AsNoTracking().Where(s => s.TaskId == taskId).OrderBy(s => s.StartedAt).ToListAsync());

    [Fact]
    public async Task AC_WORK_1_parallel_starts_create_exactly_one_session()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/start")));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Single(await SessionsAsync(Id(task)));
    }

    [Fact]
    public async Task AC_WORK_2_starting_a_second_task_while_one_runs_is_rejected_and_names_the_running_task()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var first = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id, "First");
        var second = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id, "Second");
        Assert.Equal(HttpStatusCode.OK, (await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(first)}/start")).StatusCode);

        var response = await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(second)}/start");

        var problem = await response.JsonAsync();
        Assert.Equal("work.session_active", problem.GetProperty("code").GetString());
        Assert.Contains(first.GetProperty("key").GetString()!, problem.GetProperty("title").GetString(), StringComparison.Ordinal);
        Assert.Empty(await SessionsAsync(Id(second)));
    }

    [Fact]
    public async Task AC_WORK_3_client_timestamps_are_ignored()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);

        await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/start", new { startedAt = "2020-01-01T00:00:00Z" });

        var session = Assert.Single(await SessionsAsync(Id(task)));
        Assert.True(session.StartedAt >= before, $"Expected server time, got {session.StartedAt:O}.");
    }

    [Fact]
    public async Task AC_WORK_4_only_the_assignee_can_start_even_a_team_leader_gets_403()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);

        var byLead = await scenario.Tl.PostAsync($"/api/v1/tasks/{Id(task)}/start");
        var byManager = await scenario.Pm.PostAsync($"/api/v1/tasks/{Id(task)}/start");
        var byOtherDeveloper = await scenario.Dev2.PostAsync($"/api/v1/tasks/{Id(task)}/start");

        Assert.Equal(HttpStatusCode.Forbidden, byLead.StatusCode);
        Assert.Equal("work.not_assignee", await byLead.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, byManager.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, byOtherDeveloper.StatusCode); // cannot even see it
        Assert.Empty(await SessionsAsync(Id(task)));
    }

    [Fact]
    public async Task Pause_resume_and_stop_close_sessions_with_reasons_and_time_adds_up()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        var path = $"/api/v1/tasks/{Id(task)}";

        Assert.Equal("InProgress", (await (await scenario.Dev.PostAsync($"{path}/start")).JsonAsync()).GetProperty("status").GetString());
        var active = await scenario.Dev.GetJsonAsync<JsonElement>("/api/v1/me/active-session");
        Assert.Equal("Paused", (await (await scenario.Dev.PostAsync($"{path}/pause", new { reason = "Lunch" })).JsonAsync()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await scenario.Dev.GetAsync("/api/v1/me/active-session")).StatusCode);
        Assert.Equal("InProgress", (await (await scenario.Dev.PostAsync($"{path}/resume")).JsonAsync()).GetProperty("status").GetString());
        var noReason = await scenario.Dev.PostAsync($"{path}/stop", new { reason = "" });
        var stopped = await scenario.Dev.PostAsync($"{path}/stop", new { reason = "End of day" });

        Assert.Equal(task.GetProperty("key").GetString(), active.GetProperty("taskKey").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal("Stopped", (await stopped.JsonAsync()).GetProperty("status").GetString());
        var sessions = await SessionsAsync(Id(task));
        Assert.Equal([SessionEndReason.Paused, SessionEndReason.Stopped], sessions.Select(s => s.EndReason!.Value));

        // Two sessions of 30 minutes plus a +15 minute adjustment.
        await factory.WithDbAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE time.work_sessions SET ended_at = started_at + interval '30 minutes', duration_seconds = 1800 WHERE task_id = {Id(task)}",
            TestContext.Current.CancellationToken));
        var adjusted = await scenario.Tl.PostAsync($"{path}/time-adjustments", new { userId = scenario.DevMember.Id, minutes = 15, reason = "Forgot to start the timer" });
        var time = await scenario.Dev.GetJsonAsync<JsonElement>($"{path}/time");

        Assert.Equal(HttpStatusCode.OK, adjusted.StatusCode);
        Assert.Equal(4500, time.GetProperty("spentSeconds").GetInt64());
        Assert.Equal(JsonValueKind.Null, time.GetProperty("percentOfAllocation").ValueKind); // not estimated: no percentage
        Assert.Equal(2, time.GetProperty("sessions").GetArrayLength());
        Assert.Equal(4500, (await scenario.Dev.GetJsonAsync<JsonElement>(path)).GetProperty("spentSeconds").GetInt64());
    }

    [Fact]
    public async Task Time_over_the_allocation_is_reported_as_overrun_without_blocking_work()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id, fields: new { allocatedMinutes = 60 });
        var path = $"/api/v1/tasks/{Id(task)}";
        await scenario.Dev.PostAsync($"{path}/start");
        await factory.WithDbAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE time.work_sessions SET started_at = now() - interval '90 minutes' WHERE task_id = {Id(task)}",
            TestContext.Current.CancellationToken));

        var time = await scenario.Tl.GetJsonAsync<JsonElement>($"{path}/time");
        var view = await scenario.Dev.GetJsonAsync<JsonElement>(path);

        Assert.InRange(time.GetProperty("overrunSeconds").GetInt64(), 1795, 1900);
        Assert.InRange(time.GetProperty("percentOfAllocation").GetInt32(), 149, 152);
        Assert.True(view.GetProperty("flags").GetProperty("overAllocated").GetBoolean());
        Assert.Equal("InProgress", view.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Idempotency_key_replays_the_first_response_instead_of_acting_again()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        var key = Guid.NewGuid().ToString();
        scenario.Dev.Http.DefaultRequestHeaders.Add("Idempotency-Key", key);

        var first = await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/start");
        var replay = await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/start");
        var otherEndpoint = await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/pause");
        scenario.Dev.Http.DefaultRequestHeaders.Remove("Idempotency-Key");
        var withoutKey = await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/start");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal((await first.JsonAsync()).GetProperty("version").GetUInt32(), (await replay.JsonAsync()).GetProperty("version").GetUInt32());
        Assert.Equal("idempotency.key_reused", await otherEndpoint.ProblemCodeAsync());
        Assert.Equal("task.invalid_transition", await withoutKey.ProblemCodeAsync());
        Assert.Single(await SessionsAsync(Id(task)));
    }

    [Fact]
    public async Task Pause_reason_is_required_when_the_setting_says_so()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/start");

        var invalid = await admin.PutAsync("/api/v1/settings/work", new { maxSessionHours = 30, pauseReasonRequired = true, allocationWarningPercent = 80 });
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync("/api/v1/settings/work", new { maxSessionHours = 10, pauseReasonRequired = true, allocationWarningPercent = 80 })).StatusCode);
        try
        {
            var withoutReason = await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/pause");
            var withReason = await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/pause", new { reason = "Meeting" });

            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, withoutReason.StatusCode);
            Assert.Equal(HttpStatusCode.OK, withReason.StatusCode);
        }
        finally
        {
            await admin.PutAsync("/api/v1/settings/work", new { maxSessionHours = 10, pauseReasonRequired = false, allocationWarningPercent = 80 });
        }
    }

    [Fact]
    public async Task Cancelling_a_running_task_and_deactivating_its_assignee_close_their_sessions()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);
        var cancelled = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id, "To cancel");
        var interrupted = await scenario.CreateAssignedTaskAsync(scenario.Dev2Member.Id, "Interrupted");
        await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(cancelled)}/start");
        await scenario.Dev2.PostAsync($"/api/v1/tasks/{Id(interrupted)}/start");

        await scenario.Pm.PostAsync($"/api/v1/tasks/{Id(cancelled)}/cancel", new { reason = "Duplicate" });
        await admin.PostAsync($"/api/v1/members/{scenario.Dev2Member.Id}/deactivate");

        Assert.Equal(SessionEndReason.Cancelled, Assert.Single(await SessionsAsync(Id(cancelled))).EndReason);
        Assert.Equal(SessionEndReason.MemberDeactivated, Assert.Single(await SessionsAsync(Id(interrupted))).EndReason);
        Assert.Equal("Stopped", (await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/tasks/{Id(interrupted)}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task AC_TIME_2_sessions_longer_than_the_maximum_are_auto_closed_at_the_maximum()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/start");
        await factory.WithDbAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE time.work_sessions SET started_at = now() - interval '11 hours' WHERE task_id = {Id(task)}",
            TestContext.Current.CancellationToken));

        var closed = await factory.Services.GetRequiredService<SessionMonitorWorker>().RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.True(closed >= 1);
        var session = Assert.Single(await SessionsAsync(Id(task)));
        Assert.Equal(SessionEndReason.AutoClosed, session.EndReason);
        Assert.Equal(10 * 3600, session.DurationSeconds);
        var view = await scenario.Dev.GetJsonAsync<JsonElement>($"/api/v1/tasks/{Id(task)}");
        Assert.Equal("Stopped", view.GetProperty("status").GetString());
        var history = await scenario.Dev.GetJsonAsync<JsonElement>($"/api/v1/tasks/{Id(task)}/history");
        Assert.Contains(history.EnumerateArray(), e => e.GetProperty("comment").GetString()?.Contains("auto-closed", StringComparison.Ordinal) == true
            && e.GetProperty("actor").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Time_adjustments_need_time_adjust_a_reason_and_someone_who_worked_on_the_task()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        var path = $"/api/v1/tasks/{Id(task)}/time-adjustments";

        var byDeveloper = await scenario.Dev.PostAsync(path, new { userId = scenario.DevMember.Id, minutes = 30, reason = "Mine" });
        var stranger = await scenario.Tl.PostAsync(path, new { userId = scenario.Dev2Member.Id, minutes = 30, reason = "Wrong person" });
        var zero = await scenario.Tl.PostAsync(path, new { userId = scenario.DevMember.Id, minutes = 0, reason = "Nothing" });
        var valid = await scenario.Tl.PostAsync(path, new { userId = scenario.DevMember.Id, minutes = -10, reason = "Counted a break" });
        var viewerTime = await scenario.Viewer.GetAsync($"/api/v1/tasks/{Id(task)}/time");

        Assert.Equal(HttpStatusCode.Forbidden, byDeveloper.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, stranger.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal(0, (await valid.JsonAsync()).GetProperty("spentSeconds").GetInt64()); // never negative
        Assert.Equal(HttpStatusCode.Forbidden, viewerTime.StatusCode);
    }
}
