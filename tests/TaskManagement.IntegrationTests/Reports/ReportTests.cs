using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskManagement.Domain.Time;
using TaskManagement.Infrastructure.Jobs;
using TaskManagement.IntegrationTests.Infrastructure;
using TaskManagement.IntegrationTests.Projects;

namespace TaskManagement.IntegrationTests.Reports;

[Collection(ApiCollection.Name)]
public sealed class ReportTests(ApiFactory factory)
{
    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static string Day(int offsetFromToday) => Today.AddDays(offsetFromToday).ToString("yyyy-MM-dd");

    private static DateTimeOffset At(int dayOffset, int hour) =>
        new(Today.AddDays(dayOffset).ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);

    private static List<JsonElement> Rows(JsonElement report) => [.. report.GetProperty("rows").EnumerateArray()];

    /// <summary>The cell of <paramref name="column"/> in <paramref name="row"/>, found by the column key.</summary>
    private static JsonElement Cell(JsonElement report, JsonElement row, string column)
    {
        var index = report.GetProperty("columns").EnumerateArray().Select((c, i) => (c, i)).First(x => x.c.GetProperty("key").GetString() == column).i;
        return row[index];
    }

    private static string TaskKey(JsonElement report, JsonElement row) => Cell(report, row, "task").GetProperty("key").GetString()!;

    /// <summary>A finished work session in the past (members in tests use UTC).</summary>
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
    public async Task FR_TIME_10_time_is_clipped_to_the_period_split_by_day_and_includes_adjustments()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id, "Timesheet task");
        // 23:00 three days ago until 01:00 two days ago: one hour on each day.
        await AddSessionAsync(Id(task), scenario.DevMember.Id, At(-3, 23), At(-2, 1));
        var adjusted = await scenario.Tl.PostAsync($"/api/v1/tasks/{Id(task)}/time-adjustments",
            new { userId = scenario.DevMember.Id, minutes = 30, reason = "Forgot the timer" });
        Assert.Equal(HttpStatusCode.OK, adjusted.StatusCode);
        var path = $"/api/v1/reports/time?projectId={scenario.ProjectId}";

        var byDay = await scenario.Pm.GetJsonAsync<JsonElement>($"{path}&from={Day(-3)}&to={Day(-2)}&groupBy=day");
        var days = Rows(byDay);
        Assert.Equal([Day(-3), Day(-2)], days.Select(r => Cell(byDay, r, "date").GetString()));
        Assert.All(days, r => Assert.Equal(3600, Cell(byDay, r, "time").GetInt64()));
        Assert.Equal("UTC", byDay.GetProperty("timeZone").GetString());

        var oneDay = await scenario.Pm.GetJsonAsync<JsonElement>($"{path}&from={Day(-2)}&to={Day(-2)}");
        Assert.Equal(3600, Cell(oneDay, Assert.Single(Rows(oneDay)), "time").GetInt64());

        var withAdjustment = await scenario.Pm.GetJsonAsync<JsonElement>($"{path}&from={Day(-3)}&to={Day(0)}&groupBy=member");
        var member = Assert.Single(Rows(withAdjustment));
        Assert.Equal(7200 + 1800, Cell(withAdjustment, member, "time").GetInt64());
    }

    [Fact]
    public async Task FR_RPT_06_reports_only_include_what_the_member_may_see()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        using var other = await ProjectScenario.CreateAsync(factory);
        var mine = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id, "Dev task", new { dueDate = Day(-2) });
        var theirs = await scenario.CreateAssignedTaskAsync(scenario.Dev2Member.Id, "Dev2 task", new { dueDate = Day(-2) });
        var elsewhere = await other.CreateAssignedTaskAsync(other.DevMember.Id, "Other project task", new { dueDate = Day(-2) });
        await AddSessionAsync(Id(mine), scenario.DevMember.Id, At(-1, 9), At(-1, 10));
        await AddSessionAsync(Id(theirs), scenario.Dev2Member.Id, At(-1, 9), At(-1, 11));
        await AddSessionAsync(Id(elsewhere), other.DevMember.Id, At(-1, 9), At(-1, 12));
        await RunDueDatesAsync();

        // The PM sees the whole project and nothing of the other one.
        var pmOverdue = await scenario.Pm.GetJsonAsync<JsonElement>("/api/v1/reports/overdue");
        var pmKeys = Rows(pmOverdue).Select(r => TaskKey(pmOverdue, r)).ToList();
        Assert.Contains(mine.GetProperty("key").GetString(), pmKeys);
        Assert.Contains(theirs.GetProperty("key").GetString(), pmKeys);
        Assert.DoesNotContain(elsewhere.GetProperty("key").GetString(), pmKeys);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Pm.GetAsync($"/api/v1/reports/overdue?projectId={other.ProjectId}")).StatusCode);

        // A developer (Own scope) sees only tasks they are or were assigned to.
        var devTime = await scenario.Dev.GetJsonAsync<JsonElement>($"/api/v1/reports/time?from={Day(-1)}&to={Day(-1)}");
        Assert.Equal(mine.GetProperty("key").GetString(), TaskKey(devTime, Assert.Single(Rows(devTime))));
        var devOverdue = await scenario.Dev.GetJsonAsync<JsonElement>("/api/v1/reports/overdue");
        Assert.Equal(mine.GetProperty("key").GetString(), TaskKey(devOverdue, Assert.Single(Rows(devOverdue))));

        // A viewer has reports.view but no time.view or delays.view: those reports are not offered and answer 403.
        var viewerReports = (await scenario.Viewer.GetJsonAsync<JsonElement>("/api/v1/reports")).EnumerateArray().Select(r => r.GetProperty("name").GetString()).ToList();
        Assert.Equal(["overdue", "extensions", "revisions"], viewerReports);
        Assert.Equal(HttpStatusCode.Forbidden, (await scenario.Viewer.GetAsync("/api/v1/reports/time")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await scenario.Viewer.GetAsync("/api/v1/reports/delays")).StatusCode);
        var viewerOverdue = await scenario.Viewer.GetJsonAsync<JsonElement>($"/api/v1/reports/overdue?projectId={scenario.ProjectId}");
        Assert.Equal(2, Rows(viewerOverdue).Count);

        // An Admin (Global) sees every project.
        using var admin = await factory.SignedInClientAsync(Domain.Access.SystemRoles.AdminId);
        var adminTime = await admin.GetJsonAsync<JsonElement>($"/api/v1/reports/time?projectId={other.ProjectId}&from={Day(-1)}&to={Day(-1)}");
        Assert.Equal(3 * 3600, Cell(adminTime, Assert.Single(Rows(adminTime)), "time").GetInt64());
    }

    [Fact]
    public async Task FR_RPT_07_csv_export_escapes_formulas_is_audited_and_needs_reports_export()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id, "=HYPERLINK(\"http://evil.example\",\"Click\")", new { allocatedMinutes = 60 });
        var path = $"/api/v1/reports/allocated-vs-actual?projectId={scenario.ProjectId}&format=csv";

        var response = await scenario.Pm.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.StartsWith("allocated-vs-actual-", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName);
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Project,Task,Title,Assignee,Status,Allocated (hours),Spent (hours),Overrun (hours),% used", lines[0]);
        var row = Assert.Single(lines[1..]);
        Assert.Contains(",\"'=HYPERLINK(\"\"http://evil.example\"\",\"\"Click\"\")\",", row); // edge case 38
        Assert.Contains(",1.00,0.00,0.00,0", row);

        var audited = await factory.WithDbAsync(db => db.AuditEntries.AsNoTracking()
            .CountAsync(e => e.Action == "report.exported" && e.EntityId == "allocated-vs-actual" && e.ProjectId == scenario.ProjectId && e.ActorId == scenario.PmMember.Id));
        Assert.Equal(1, audited);

        // Developers may view their own report rows but not export (permissions matrix: reports.export).
        Assert.Equal(HttpStatusCode.OK, (await scenario.Dev.GetAsync("/api/v1/reports/allocated-vs-actual")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await scenario.Dev.GetAsync("/api/v1/reports/allocated-vs-actual?format=csv")).StatusCode);
        var devReports = await scenario.Dev.GetJsonAsync<JsonElement>("/api/v1/reports");
        Assert.All(devReports.EnumerateArray(), r => Assert.False(r.GetProperty("canExport").GetBoolean()));
    }

    [Fact]
    public async Task Allocated_vs_actual_shows_overrun_and_percentage()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id, "Over budget", new { allocatedMinutes = 60 });
        await AddSessionAsync(Id(task), scenario.DevMember.Id, At(-1, 8), At(-1, 9).AddMinutes(30));

        var report = await scenario.Tl.GetJsonAsync<JsonElement>($"/api/v1/reports/allocated-vs-actual?projectId={scenario.ProjectId}");

        var row = Assert.Single(Rows(report));
        Assert.Equal(3600, Cell(report, row, "allocated").GetInt64());
        Assert.Equal(5400, Cell(report, row, "spent").GetInt64());
        Assert.Equal(1800, Cell(report, row, "overrun").GetInt64());
        Assert.Equal(150, Cell(report, row, "percent").GetInt32());
        Assert.Equal("Ready to start", Cell(report, row, "status").GetString());
    }

    [Fact]
    public async Task Extension_revision_and_delay_reports_list_what_happened_in_the_period()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id, "Eventful");
        var path = $"/api/v1/tasks/{Id(task)}";
        Assert.Equal(HttpStatusCode.OK, (await scenario.Dev.PostAsync($"{path}/start")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await scenario.Dev.PostAsync($"{path}/delay-reports", new { category = "BlockedByDependency", description = "Waiting for the API" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await scenario.Dev.PostAsync($"{path}/submit-review", new { summary = "Done" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await scenario.Tl.PostAsync($"{path}/request-revision", new { comment = "Handle errors", algorithmReapprovalRequired = false })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await scenario.Dev.PostAsync($"{path}/extension-requests", new { proposedDueDate = Day(30), reason = "More work after the revision" })).StatusCode);
        var query = $"?projectId={scenario.ProjectId}&from={Day(0)}&to={Day(0)}";

        var extensions = await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/reports/extensions{query}");
        var extension = Assert.Single(Rows(extensions));
        Assert.Equal(Day(30), Cell(extensions, extension, "proposedDueDate").GetString());
        Assert.Equal("Pending", Cell(extensions, extension, "status").GetString());

        var revisions = await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/reports/revisions{query}");
        var revision = Assert.Single(Rows(revisions));
        Assert.Equal(1, Cell(revisions, revision, "revisions").GetInt32());
        Assert.Equal("Revision requested", Cell(revisions, revision, "status").GetString());

        var delays = await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/reports/delays{query}");
        var delay = Assert.Single(Rows(delays));
        Assert.Equal("Blocked by dependency", Cell(delays, delay, "category").GetString());
        Assert.Equal("Waiting for the API", Cell(delays, delay, "description").GetString());

        // Nothing in a period before it all happened.
        var earlier = await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/reports/delays?projectId={scenario.ProjectId}&from={Day(-9)}&to={Day(-1)}");
        Assert.Empty(Rows(earlier));
    }

    [Fact]
    public async Task Invalid_filters_are_refused()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Pm.GetAsync("/api/v1/reports/salaries")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Pm.GetAsync($"/api/v1/reports/time?from={Day(0)}&to={Day(-1)}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Pm.GetAsync($"/api/v1/reports/time?from={Day(-400)}&to={Day(0)}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Pm.GetAsync("/api/v1/reports/time?groupBy=salary")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Pm.GetAsync("/api/v1/reports/time?format=xlsx")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Pm.GetAsync($"/api/v1/reports/time?projectId={Guid.NewGuid()}")).StatusCode);
        using var anonymous = factory.CreateTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/reports/time")).StatusCode);
    }
}
