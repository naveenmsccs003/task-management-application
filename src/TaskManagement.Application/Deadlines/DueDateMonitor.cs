using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Common;
using TaskManagement.Application.Notifications;
using TaskManagement.Domain.Notifications;
using TaskManagement.Domain.Tasks;

namespace TaskManagement.Application.Deadlines;

/// <summary>
/// The due-date job (FR-DL-05, task-workflow.md §7), run hourly: keeps the Overdue flag in step with each assignee's local
/// date, notifies the assignee and Team Leaders once per day while a task is overdue, and sends one "due soon" notice when
/// the due date is today or tomorrow.
/// </summary>
public sealed class DueDateMonitor(IAppDbContext db, Notifier notifier, TimeProvider timeProvider)
{
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var tasks = await db.Tasks
            .Where(t => t.IsOverdue || (t.DueDate != null
                && t.Status != TaskItemStatus.Approved && t.Status != TaskItemStatus.Closed && t.Status != TaskItemStatus.Cancelled))
            .ToListAsync(cancellationToken);
        var assigneeIds = tasks.Where(t => t.AssigneeId != null).Select(t => t.AssigneeId!.Value).Distinct().ToList();
        var zones = await db.Users.AsNoTracking().Where(u => assigneeIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.TimeZone, cancellationToken);

        var changed = 0;
        foreach (var task in tasks)
        {
            var today = DueDates.TodayFor(task.AssigneeId is { } a ? zones.GetValueOrDefault(a) : null, now);
            if (task.RefreshOverdue(today))
            {
                changed++;
            }

            var link = $"/tasks/{task.Id}";
            if (task.TakeOverdueNotification(today))
            {
                changed++;
                var recipients = (await notifier.ProjectLeadsAsync(task.ProjectId, cancellationToken)).Concat(task.AssigneeId is { } assignee ? [assignee] : []);
                await notifier.NotifyAsync(
                    recipients, NotificationType.Overdue, $"{task.Key} is overdue",
                    $"“{task.Title}” was due {task.DueDate:yyyy-MM-dd}.", link, task.ProjectId, task.Id, cancellationToken);
            }
            else if (task.AssigneeId is { } assignee && task.TakeDueSoonNotification(today))
            {
                changed++;
                await notifier.NotifyAsync(
                    [assignee], NotificationType.DueSoon, $"{task.Key} is due {(task.DueDate == today ? "today" : "tomorrow")}",
                    $"“{task.Title}” is due {task.DueDate:yyyy-MM-dd}.", link, task.ProjectId, task.Id, cancellationToken);
            }
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return changed;
    }
}
