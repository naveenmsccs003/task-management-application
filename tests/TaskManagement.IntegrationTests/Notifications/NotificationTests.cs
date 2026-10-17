using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Notifications;
using TaskManagement.Infrastructure.Jobs;
using TaskManagement.IntegrationTests.Infrastructure;
using TaskManagement.IntegrationTests.Projects;

namespace TaskManagement.IntegrationTests.Notifications;

[Collection(ApiCollection.Name)]
public sealed class NotificationTests(ApiFactory factory)
{
    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    private Task<List<Notification>> NotificationsForAsync(Guid userId, Guid? taskId = null) =>
        factory.WithDbAsync(db => db.Notifications.AsNoTracking()
            .Where(n => n.RecipientId == userId && (taskId == null || n.TaskId == taskId))
            .OrderBy(n => n.CreatedAt)
            .ToListAsync());

    private Task<int> RunMonitorAsync() =>
        factory.Services.GetRequiredService<SessionMonitorWorker>().RunOnceAsync(TestContext.Current.CancellationToken);

    private Task BackdateSessionAsync(Guid taskId, int minutes) =>
        factory.WithDbAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE time.work_sessions SET started_at = now() - make_interval(mins => {minutes}) WHERE task_id = {taskId} AND ended_at IS NULL",
            TestContext.Current.CancellationToken));

    [Fact]
    public async Task FR_ASG_06_assignment_notifies_the_assignee_and_team_leaders_but_not_the_person_assigning()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);

        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id, "Notify me");
        await factory.FlushEmailsAsync();

        var devNotification = Assert.Single(await NotificationsForAsync(scenario.DevMember.Id, Id(task)));
        Assert.Equal(NotificationType.TaskAssigned, devNotification.Type);
        Assert.Equal($"{task.GetProperty("key").GetString()} was assigned to you", devNotification.Title);
        Assert.Equal($"/tasks/{Id(task)}", devNotification.Link);
        Assert.Contains("Test Developer", Assert.Single(await NotificationsForAsync(scenario.TlMember.Id, Id(task))).Title, StringComparison.Ordinal);
        Assert.Empty(await NotificationsForAsync(scenario.PmMember.Id, Id(task)));
        Assert.Empty(await NotificationsForAsync(scenario.Dev2Member.Id, Id(task)));
        Assert.Contains(factory.Emails.To(scenario.DevMember.Email), m => m.Subject!.StartsWith($"{task.GetProperty("key").GetString()} was assigned to you", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Algorithm_events_notify_team_leaders_on_submit_and_the_assignee_on_decisions()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory, algorithmRequiredDefault: true);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        var path = $"/api/v1/tasks/{Id(task)}/algorithm";

        await scenario.Dev.PostAsync(path, new { content = "Plan A" });
        var submission = Id((await scenario.Tl.GetJsonAsync<JsonElement>(path))[0]);
        await scenario.Tl.PostAsync($"{path}/{submission}/reject", new { comment = "Too vague." });

        Assert.Contains(await NotificationsForAsync(scenario.TlMember.Id, Id(task)), n => n.Type == NotificationType.AlgorithmSubmitted);
        var rejected = (await NotificationsForAsync(scenario.DevMember.Id, Id(task))).Single(n => n.Type == NotificationType.AlgorithmRejected);
        Assert.EndsWith("Too vague.", rejected.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FR_NOT_02_turning_off_a_category_stops_its_emails_but_not_in_app_notifications()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var mandatory = await scenario.Dev.PutAsync("/api/v1/me/notification-preferences", new { preferences = new[] { new { category = "Account", emailEnabled = false } } });
        var saved = await scenario.Dev.PutAsync("/api/v1/me/notification-preferences", new { preferences = new[] { new { category = "Assignment", emailEnabled = false } } });
        var before = factory.Emails.To(scenario.DevMember.Email).Count;

        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        await factory.FlushEmailsAsync();

        Assert.Equal(HttpStatusCode.BadRequest, mandatory.StatusCode);
        var preferences = (await saved.JsonAsync()).EnumerateArray().ToDictionary(p => p.GetProperty("category").GetString()!, p => p.GetProperty("emailEnabled").GetBoolean());
        Assert.False(preferences["Assignment"]);
        Assert.True(preferences["Account"]);
        Assert.Single(await NotificationsForAsync(scenario.DevMember.Id, Id(task)));
        Assert.Equal(before, factory.Emails.To(scenario.DevMember.Email).Count);
    }

    [Fact]
    public async Task Members_list_read_and_count_only_their_own_notifications()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id, "One");
        await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id, "Two");
        var tlNotification = (await NotificationsForAsync(scenario.TlMember.Id)).First();

        var count = await scenario.Dev.GetJsonAsync<JsonElement>("/api/v1/notifications/unread-count");
        var list = await scenario.Dev.GetJsonAsync<JsonElement>("/api/v1/notifications?unreadOnly=true");
        var first = Id(list.GetProperty("items")[0]);
        var readOne = await scenario.Dev.PostAsync($"/api/v1/notifications/{first}/read");
        var someoneElses = await scenario.Dev.PostAsync($"/api/v1/notifications/{tlNotification.Id}/read");
        var afterOne = await scenario.Dev.GetJsonAsync<JsonElement>("/api/v1/notifications/unread-count");
        await scenario.Dev.PostAsync("/api/v1/notifications/read-all");
        var afterAll = await scenario.Dev.GetJsonAsync<JsonElement>("/api/v1/notifications/unread-count");

        Assert.Equal(2, count.GetProperty("count").GetInt32());
        Assert.StartsWith("“Two”", list.GetProperty("items")[0].GetProperty("body").GetString(), StringComparison.Ordinal); // newest first
        Assert.Equal(HttpStatusCode.NoContent, readOne.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, someoneElses.StatusCode);
        Assert.Null((await NotificationsForAsync(scenario.TlMember.Id)).First().ReadAt);
        Assert.Equal(1, afterOne.GetProperty("count").GetInt32());
        Assert.Equal(0, afterAll.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task AC_TIME_1_reaching_the_allocation_notifies_the_assignee_and_team_leader_exactly_once()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id, fields: new { allocatedMinutes = 120 });
        await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/start");
        await BackdateSessionAsync(Id(task), 121);

        await RunMonitorAsync();
        await RunMonitorAsync();

        Assert.Single(await NotificationsForAsync(scenario.DevMember.Id, Id(task)), n => n.Type == NotificationType.AllocationReached);
        Assert.Single(await NotificationsForAsync(scenario.TlMember.Id, Id(task)), n => n.Type == NotificationType.AllocationReached);
        Assert.DoesNotContain(await NotificationsForAsync(scenario.DevMember.Id, Id(task)), n => n.Type == NotificationType.AllocationWarning);
    }

    [Fact]
    public async Task The_warning_goes_to_the_assignee_once_and_a_new_allocation_is_judged_again()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id, fields: new { allocatedMinutes = 120 });
        await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/start");
        await BackdateSessionAsync(Id(task), 100);

        await RunMonitorAsync();
        await RunMonitorAsync();
        var current = await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/tasks/{Id(task)}");
        await scenario.Pm.PatchAsync($"/api/v1/tasks/{Id(task)}", new
        {
            title = current.GetProperty("title").GetString(),
            description = "",
            dueDate = current.GetProperty("dueDate").GetString(),
            allocatedMinutes = 90,
            labels = Array.Empty<string>(),
            version = current.GetProperty("version").GetUInt32(),
        });
        await RunMonitorAsync();

        var devNotifications = await NotificationsForAsync(scenario.DevMember.Id, Id(task));
        Assert.Single(devNotifications, n => n.Type == NotificationType.AllocationWarning);
        Assert.Single(devNotifications, n => n.Type == NotificationType.AllocationReached);
        Assert.DoesNotContain(await NotificationsForAsync(scenario.TlMember.Id, Id(task)), n => n.Type == NotificationType.AllocationWarning);
    }

    [Fact]
    public async Task AC_TIME_2_an_auto_closed_session_notifies_the_member_and_team_leader()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/start");
        await BackdateSessionAsync(Id(task), 11 * 60);

        await RunMonitorAsync();

        Assert.Single(await NotificationsForAsync(scenario.DevMember.Id, Id(task)), n => n.Type == NotificationType.SessionAutoClosed);
        Assert.Single(await NotificationsForAsync(scenario.TlMember.Id, Id(task)), n => n.Type == NotificationType.SessionAutoClosed);
    }

    [Fact]
    public async Task Changing_a_password_sends_a_security_email()
    {
        var member = await factory.CreateMemberAsync(SystemRoles.DeveloperId);
        using var client = factory.CreateTestClient();
        await client.SignInAsync(member.Email, member.Password);

        var response = await client.PostAsync("/api/v1/auth/change-password", new { currentPassword = member.Password, newPassword = "violet-harbor-engine-73" });
        await factory.FlushEmailsAsync();

        Assert.True(response.IsSuccessStatusCode, $"Change password returned {(int)response.StatusCode}.");
        Assert.Contains(factory.Emails.To(member.Email), m => m.Subject!.Contains("password was changed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task System_status_shows_jobs_and_the_email_queue_to_admins_only()
    {
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);
        using var developer = await factory.SignedInClientAsync(SystemRoles.DeveloperId);
        await RunMonitorAsync();
        await factory.Services.GetRequiredService<CleanupWorker>().RunOnceAsync(TestContext.Current.CancellationToken);

        var status = await admin.GetJsonAsync<JsonElement>("/api/v1/admin/system-status");
        var forbidden = await developer.GetAsync("/api/v1/admin/system-status");

        var jobs = status.GetProperty("jobs").EnumerateArray().Select(j => j.GetProperty("jobName").GetString()).ToList();
        Assert.Contains("session-monitor", jobs);
        Assert.Contains("cleanup", jobs);
        Assert.True(status.GetProperty("email").TryGetProperty("pending", out _));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Cleanup_removes_notifications_older_than_180_days()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        var notification = Assert.Single(await NotificationsForAsync(scenario.DevMember.Id, Id(task)));
        await factory.WithDbAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE notifications.notifications SET created_at = now() - interval '181 days' WHERE id = {notification.Id}",
            TestContext.Current.CancellationToken));

        await factory.Services.GetRequiredService<CleanupWorker>().RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Empty(await NotificationsForAsync(scenario.DevMember.Id, Id(task)));
        Assert.Single(await NotificationsForAsync(scenario.TlMember.Id, Id(task)));
    }

    [Fact]
    public async Task FR_NOT_07_notifications_arrive_over_signalr_without_a_refresh()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var cookies = new System.Net.CookieContainer();
        using var http = factory.CreateDefaultClient(new Uri("https://localhost"), new CookieContainerHandler(cookies));
        using var devClient = new TestClient(http);
        await devClient.SignInAsync(scenario.DevMember.Email, scenario.DevMember.Password);
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri("https://localhost/hubs/notifications"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.Headers["Cookie"] = cookies.GetCookieHeader(new Uri("https://localhost"));
            })
            .Build();
        connection.On<JsonElement>("notification", n => received.TrySetResult(n));
        await connection.StartAsync(TestContext.Current.CancellationToken);

        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id, "Live");
        var pushed = await received.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        Assert.Equal($"/tasks/{Id(task)}", pushed.GetProperty("link").GetString());
        Assert.Equal("TaskAssigned", pushed.GetProperty("type").GetString());
    }

    [Fact]
    public async Task The_notification_hub_refuses_anonymous_connections()
    {
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var response = await client.PostAsync(new Uri("/hubs/notifications/negotiate?negotiateVersion=1", UriKind.Relative), null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
