using TaskManagement.Domain.Notifications;
using TaskManagement.Domain.Tasks;

namespace TaskManagement.Domain.Tests.Notifications;

public sealed class NotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    private static TaskItem RunningTask(int? allocatedMinutes)
    {
        var details = new TaskDetails("Build login", null, TaskType.Feature, TaskPriority.High, null, new DateOnly(2026, 11, 1), allocatedMinutes, false, []);
        return TaskItem.Create(Guid.CreateVersion7(), "TMA", 1, details, Guid.CreateVersion7(), Now);
    }

    [Fact]
    public void Notifications_start_unread_are_trimmed_and_only_link_inside_the_app()
    {
        var notification = Notification.Create(Guid.CreateVersion7(), NotificationType.TaskAssigned, "  TMA-1 was assigned to you ", new string('x', 1200), "/tasks/1", null, null, Now);

        Assert.False(notification.IsRead);
        Assert.Equal("TMA-1 was assigned to you", notification.Title);
        Assert.Equal(Notification.BodyMaxLength, notification.Body.Length);
        Assert.Throws<ArgumentException>(() =>
            Notification.Create(Guid.CreateVersion7(), NotificationType.TaskAssigned, "Phish", "", "https://evil.example.com", null, null, Now));

        notification.MarkRead(Now.AddMinutes(5));
        notification.MarkRead(Now.AddMinutes(10));
        Assert.Equal(Now.AddMinutes(5), notification.ReadAt);
    }

    [Fact]
    public void Account_emails_are_mandatory_and_other_categories_are_optional()
    {
        var user = Guid.CreateVersion7();

        Assert.Throws<ArgumentException>(() => NotificationPreference.Create(user, NotificationCategory.Account, false));
        var assignment = NotificationPreference.Create(user, NotificationCategory.Assignment, false);
        Assert.False(assignment.EmailEnabled);
        Assert.All(Enum.GetValues<NotificationType>(), t => Assert.False(NotificationCatalog.IsMandatory(NotificationCatalog.CategoryOf(t))));
    }

    [Fact]
    public void Allocation_warning_and_limit_are_each_sent_once_per_revision()
    {
        var task = RunningTask(allocatedMinutes: 120);

        Assert.Null(task.CheckAllocation(spentSeconds: 90 * 60, warningPercent: 80));
        Assert.Equal(AllocationAlert.Warning, task.CheckAllocation(96 * 60, 80));
        Assert.Null(task.CheckAllocation(100 * 60, 80));
        Assert.Equal(AllocationAlert.Reached, task.CheckAllocation(120 * 60, 80));
        Assert.Null(task.CheckAllocation(150 * 60, 80));
    }

    [Fact]
    public void Jumping_past_the_limit_sends_only_the_limit_notification()
    {
        var task = RunningTask(allocatedMinutes: 60);

        Assert.Equal(AllocationAlert.Reached, task.CheckAllocation(70 * 60, 80));
        Assert.Null(task.CheckAllocation(71 * 60, 80));
    }

    [Fact]
    public void WQ2_changing_the_allocation_resets_the_notifications_and_unestimated_tasks_never_notify()
    {
        var task = RunningTask(allocatedMinutes: 60);
        task.CheckAllocation(60 * 60, 80);

        task.UpdateDetails(new TaskDetails("Build login", null, TaskType.Feature, TaskPriority.High, null, task.DueDate, 50, false, []), Guid.CreateVersion7(), Now);

        Assert.Equal(AllocationAlert.Reached, task.CheckAllocation(60 * 60, 80));
        Assert.Null(RunningTask(allocatedMinutes: null).CheckAllocation(99_999, 80));
    }
}
