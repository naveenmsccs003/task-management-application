using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Application.Settings;
using TaskManagement.Domain.Time;

namespace TaskManagement.Application.Work;

/// <summary>
/// The once-a-minute job (task-workflow.md §4.6): closes sessions longer than the maximum length as Auto-closed
/// (FR-TIME-09) and stops their tasks, and removes expired idempotency records. Allocated-hour notifications join this
/// job in the notifications phase.
/// </summary>
public sealed partial class SessionMonitor(
    IAppDbContext db,
    SettingsService settingsService,
    IdempotencyStore idempotency,
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
        }

        if (expired.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await idempotency.DeleteExpiredAsync(cancellationToken);
        return expired.Count;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Auto-closed work session {SessionId} on {TaskKey}.")]
    private static partial void LogAutoClosed(ILogger logger, Guid sessionId, string taskKey);
}
