using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Application.Notifications;
using TaskManagement.Application.Settings;
using TaskManagement.Application.Time;
using TaskManagement.Domain.Notifications;
using TaskManagement.Domain.Tasks;
using TaskManagement.Domain.Time;

namespace TaskManagement.Application.Work;

/// <summary>
/// The once-a-minute job (task-workflow.md §4.6): closes sessions longer than the maximum length as Auto-closed
/// (FR-TIME-09) and stops their tasks, sends the allocated-hour notifications once per revision (FR-TIME-06/07, edge
/// case 14), and removes expired idempotency records.
/// </summary>
public sealed partial class SessionMonitor(
    IAppDbContext db,
    SettingsService settingsService,
    IdempotencyStore idempotency,
    TimeService time,
    Notifier notifier,
    IAuditLog auditLog,
    TimeProvider timeProvider,
    ILogger<SessionMonitor> logger)
{
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var settings = await settingsService.GetWorkAsync(cancellationToken);
        var maxLength = TimeSpan.FromHours(settings.MaxSessionHours);
        var cutoff = timeProvider.GetUtcNow() - maxLength;

        var expired = await db.WorkSessions.Where(s => s.EndedAt == null && s.StartedAt < cutoff).ToListAsync(cancellationToken);
        foreach (var session in expired)
        {
            // The session counts up to the maximum length, not up to whenever the job happened to run.
            var end = session.StartedAt + maxLength;
            session.End(end, SessionEndReason.AutoClosed);
            var task = await db.Tasks.FirstAsync(t => t.Id == session.TaskId, cancellationToken);
            task.StopBySystem($"Work session auto-closed after {settings.MaxSessionHours} hours.", end);
            auditLog.Record("work_session.auto_closed", "WorkSession", session.Id.ToString(), new AuditChanges()
                .Set("taskId", task.Id)
                .Set("userId", session.UserId)
                .Set("durationSeconds", session.DurationSeconds), task.ProjectId);
            LogAutoClosed(logger, session.Id, task.Key);
            var leads = await notifier.ProjectLeadsAsync(task.ProjectId, cancellationToken);
            await notifier.NotifyAsync(
                leads.Append(session.UserId), NotificationType.SessionAutoClosed, $"Work session auto-closed on {task.Key}",
                $"A session on “{task.Title}” ran longer than {settings.MaxSessionHours} hours and was closed at that limit. The task is now stopped; a Team Leader can adjust the time.",
                $"/tasks/{task.Id}", task.ProjectId, task.Id, cancellationToken);
        }

        if (expired.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        // After the auto-closes are saved, so a session closed above no longer counts as running.
        if (await CheckAllocationsAsync(settings.AllocationWarningPercent, cancellationToken) > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await idempotency.DeleteExpiredAsync(cancellationToken);
        return expired.Count;
    }

    /// <summary>Running tasks with an allocation get the 80 % warning and the 100 % notification once per revision.</summary>
    private async Task<int> CheckAllocationsAsync(int warningPercent, CancellationToken cancellationToken)
    {
        var running = await db.Tasks
            .Where(t => t.AllocatedMinutes != null && db.WorkSessions.Any(s => s.TaskId == t.Id && s.EndedAt == null))
            .ToListAsync(cancellationToken);
        var sent = 0;
        foreach (var task in running)
        {
            var spent = await time.SpentSecondsAsync(task.Id, cancellationToken);
            var alert = task.CheckAllocation(spent, warningPercent);
            if (alert is null || task.AssigneeId is not { } assigneeId)
            {
                continue;
            }

            var hours = $"{task.AllocatedMinutes!.Value / 60.0:0.##} h";
            var link = $"/tasks/{task.Id}";
            if (alert == AllocationAlert.Warning)
            {
                await notifier.NotifyAsync(
                    [assigneeId], NotificationType.AllocationWarning, $"{task.Key} is at {warningPercent}% of its allocated time",
                    $"“{task.Title}” has used {warningPercent}% of its {hours}.", link, task.ProjectId, task.Id, cancellationToken);
            }
            else
            {
                var recipients = (await notifier.ProjectLeadsAsync(task.ProjectId, cancellationToken)).Append(assigneeId);
                await notifier.NotifyAsync(
                    recipients, NotificationType.AllocationReached, $"{task.Key} reached its allocated time",
                    $"“{task.Title}” has used all of its {hours}. Work can continue; extra time is reported as overrun.", link, task.ProjectId, task.Id, cancellationToken);
            }

            sent++;
        }

        return sent;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Auto-closed work session {SessionId} on {TaskKey}.")]
    private static partial void LogAutoClosed(ILogger logger, Guid sessionId, string taskKey);
}
