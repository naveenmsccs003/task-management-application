using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskManagement.Domain.Notifications;
using TaskManagement.Infrastructure.Jobs;
using TaskManagement.IntegrationTests.Infrastructure;
using TaskManagement.IntegrationTests.Projects;

namespace TaskManagement.IntegrationTests.Review;

[Collection(ApiCollection.Name)]
public sealed class DeadlineTests(ApiFactory factory)
{
    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    private static string Day(int offsetFromToday) => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(offsetFromToday)).ToString("yyyy-MM-dd");

    private Task<int> RunDueDatesAsync() =>
        factory.Services.GetRequiredService<DueDateWorker>().RunOnceAsync(TestContext.Current.CancellationToken);

    private Task<int> CountAsync(Guid userId, Guid taskId, NotificationType type) =>
        factory.WithDbAsync(db => db.Notifications.CountAsync(n => n.RecipientId == userId && n.TaskId == taskId && n.Type == type));

    [Fact]
    public async Task FR_DL_05_the_job_flags_overdue_tasks_and_notifies_once_a_day()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        // Two days late, so it is overdue in every time zone.
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id, fields: new { dueDate = Day(-2) });

        await RunDueDatesAsync();
        await RunDueDatesAsync();

        var view = await scenario.Dev.GetJsonAsync<JsonElement>($"/api/v1/tasks/{Id(task)}");
        Assert.True(view.GetProperty("flags").GetProperty("overdue").GetBoolean());
        Assert.Equal(1, await CountAsync(scenario.DevMember.Id, Id(task), NotificationType.Overdue));
        Assert.Equal(1, await CountAsync(scenario.TlMember.Id, Id(task), NotificationType.Overdue));
        var overdueList = await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/tasks?projectId={scenario.ProjectId}&overdue=true");
        Assert.Equal(Id(task), Id(Assert.Single(overdueList.GetProperty("items").EnumerateArray())));
    }

    [Fact]
    public async Task An_approved_extension_clears_the_overdue_flag()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id, fields: new { dueDate = Day(-2) });
        await RunDueDatesAsync();
        await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/extension-requests", new { proposedDueDate = Day(5), reason = "Blocked by the API team." });
        var requestId = Id((await scenario.Tl.GetJsonAsync<JsonElement>($"/api/v1/tasks/{Id(task)}/requests"))[0]);

        await scenario.Tl.PostAsync($"/api/v1/requests/{requestId}/approve");

        var view = await scenario.Dev.GetJsonAsync<JsonElement>($"/api/v1/tasks/{Id(task)}");
        Assert.False(view.GetProperty("flags").GetProperty("overdue").GetBoolean());
        Assert.Equal(Day(5), view.GetProperty("dueDate").GetString());
    }

    [Fact]
    public async Task A_due_soon_notice_is_sent_once_when_the_due_date_is_near()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        // Test members use the UTC time zone, so this is due tomorrow for the assignee.
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id, fields: new { dueDate = Day(1) });

        await RunDueDatesAsync();
        await RunDueDatesAsync();

        Assert.Equal(1, await CountAsync(scenario.DevMember.Id, Id(task), NotificationType.DueSoon));
    }

    [Fact]
    public async Task FR_DL_06_the_assignee_reports_delays_and_leads_are_told()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        var path = $"/api/v1/tasks/{Id(task)}/delay-reports";

        var noCategory = await scenario.Dev.PostAsync(path, new { description = "Waiting for the API" });
        var byLead = await scenario.Tl.PostAsync(path, new { category = "Other", description = "Not the assignee" });
        var reported = await scenario.Dev.PostAsync(path, new { category = "BlockedByDependency", description = "Waiting for the payments API." });
        var viewerList = await scenario.Viewer.GetAsync(path);
        var leadList = await scenario.Tl.GetJsonAsync<JsonElement>(path);

        Assert.Equal(HttpStatusCode.BadRequest, noCategory.StatusCode);
        Assert.Equal("delay.not_assignee", await byLead.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.OK, reported.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, viewerList.StatusCode);
        Assert.Equal("BlockedByDependency", Assert.Single(leadList.EnumerateArray()).GetProperty("category").GetString());
        Assert.Equal(1, await CountAsync(scenario.PmMember.Id, Id(task), NotificationType.DelayReported));
        var view = await scenario.Dev.GetJsonAsync<JsonElement>($"/api/v1/tasks/{Id(task)}");
        Assert.Equal(task.GetProperty("dueDate").GetString(), view.GetProperty("dueDate").GetString()); // due date unchanged
    }
}
