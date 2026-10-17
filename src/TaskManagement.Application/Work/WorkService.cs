using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Application.Settings;
using TaskManagement.Application.Tasks;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Common;
using TaskManagement.Domain.Projects;
using TaskManagement.Domain.Tasks;
using TaskManagement.Domain.Time;

namespace TaskManagement.Application.Work;

public sealed record PauseCommand(string? Reason);

public sealed record StopCommand(string? Reason);

/// <summary>GET /me/active-session — what the timer bar shows. Times are server UTC; the client only formats them.</summary>
public sealed record ActiveSessionDto(Guid SessionId, Guid TaskId, string TaskKey, string TaskTitle, DateTimeOffset StartedAt, DateTimeOffset ServerTime);

/// <summary>
/// Start, pause, resume and stop (T6–T9, FR-WORK-01..07). Only the current assignee acts; times come from the server clock
/// (FR-TIME-02); the database allows one active session per member and per task (FR-TIME-03), and this class turns a
/// violation of that rule into a clear 409.
/// </summary>
public sealed partial class WorkService(
    IAppDbContext db,
    TaskAccess access,
    TaskQueries queries,
    SettingsService settingsService,
    IAuditLog auditLog,
    TimeProvider timeProvider,
    ILogger<WorkService> logger)
{
    private static readonly Error NotAssignee =
        Error.Forbidden("work.not_assignee", "Only the task's assignee can start, pause, resume or stop work on it.");

    public Task<Result<TaskDto>> StartAsync(Guid taskId, CancellationToken cancellationToken) =>
        BeginAsync(taskId, TaskAction.Start, cancellationToken);

    public Task<Result<TaskDto>> ResumeAsync(Guid taskId, CancellationToken cancellationToken) =>
        BeginAsync(taskId, TaskAction.Resume, cancellationToken);

    public async Task<Result<TaskDto>> PauseAsync(Guid taskId, PauseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var settings = await settingsService.GetWorkAsync(cancellationToken);
        var errors = new FieldErrors().MaxLength("reason", command.Reason?.Trim(), TaskItem.ReasonMaxLength, "The reason");
        if (settings.PauseReasonRequired)
        {
            errors.Required("reason", command.Reason, "A pause reason");
        }

        return errors.HasErrors
            ? errors.ToError()
            : await EndAsync(taskId, SessionEndReason.Paused, (task, by, now) => task.Pause(by, command.Reason, now), cancellationToken);
    }

    public async Task<Result<TaskDto>> StopAsync(Guid taskId, StopCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = new FieldErrors()
            .Required("reason", command.Reason, "A reason")
            .MaxLength("reason", command.Reason?.Trim(), TaskItem.ReasonMaxLength, "The reason");
        return errors.HasErrors
            ? errors.ToError()
            : await EndAsync(taskId, SessionEndReason.Stopped, (task, by, now) => task.Stop(by, command.Reason!, now), cancellationToken);
    }

    public async Task<ActiveSessionDto?> ActiveSessionAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.WorkSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.EndedAt == null)
            .Join(db.Tasks, s => s.TaskId, t => t.Id, (s, t) => new ActiveSessionDto(s.Id, t.Id, t.Key, t.Title, s.StartedAt, timeProvider.GetUtcNow()))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Ends the task's active session, if any, with <paramref name="reason"/>. Used by pause/stop and by actions that
    /// interrupt work (cancel, member deactivation, the session monitor). The caller saves.
    /// </summary>
    public async Task<WorkSession?> CloseActiveSessionAsync(Guid taskId, SessionEndReason reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var session = await db.WorkSessions.FirstOrDefaultAsync(s => s.TaskId == taskId && s.EndedAt == null, cancellationToken);
        if (session is not null && !session.End(now, reason))
        {
            LogClockMovedBack(logger, session.Id);
        }

        return session;
    }

    private async Task<Result<TaskDto>> BeginAsync(Guid taskId, TaskAction action, CancellationToken cancellationToken)
    {
        var guard = await LoadForWorkAsync(taskId, cancellationToken);
        if (guard.IsFailure)
        {
            return guard.Error!;
        }

        var visible = guard.Value;
        var task = visible.Task;
        var now = timeProvider.GetUtcNow();

        // Edge case 11 / WQ3: one task at a time — say which task is still running.
        var running = await db.WorkSessions.AsNoTracking()
            .Where(s => s.UserId == visible.UserId && s.EndedAt == null && s.TaskId != task.Id)
            .Join(db.Tasks, s => s.TaskId, t => t.Id, (s, t) => t.Key)
            .FirstOrDefaultAsync(cancellationToken);
        if (running is not null)
        {
            return Error.Conflict("work.session_active", $"You are already working on {running}. Pause or stop {running} first.");
        }

        var previousStatus = task.Status;
        var result = action == TaskAction.Start ? task.Start(visible.UserId, now) : task.Resume(visible.UserId, now);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        db.WorkSessions.Add(WorkSession.Start(task.Id, visible.UserId, task.CurrentRevision, now));
        RecordStatusAudit(task, previousStatus);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (db.IsUniqueViolation(exception))
        {
            // A parallel Start won the race for the one active session the database allows (AC-WORK-1).
            db.DiscardChanges();
            return Error.Conflict("work.session_active", "Work on this task, or another of your tasks, has already started.");
        }

        return await queries.GetAsync(task.Id, cancellationToken);
    }

    private async Task<Result<TaskDto>> EndAsync(
        Guid taskId, SessionEndReason reason, Func<TaskItem, Guid, DateTimeOffset, Result> transition, CancellationToken cancellationToken)
    {
        var guard = await LoadForWorkAsync(taskId, cancellationToken);
        if (guard.IsFailure)
        {
            return guard.Error!;
        }

        var visible = guard.Value;
        var task = visible.Task;
        var now = timeProvider.GetUtcNow();
        var previousStatus = task.Status;
        var result = transition(task, visible.UserId, now);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        await CloseActiveSessionAsync(task.Id, reason, now, cancellationToken);
        RecordStatusAudit(task, previousStatus);
        await db.SaveChangesAsync(cancellationToken);
        return await queries.GetAsync(task.Id, cancellationToken);
    }

    /// <summary>404 if not visible; 403 unless the caller is the assignee with tasks.work (AC-WORK-4); 409 if read-only.</summary>
    private async Task<Result<VisibleTask>> LoadForWorkAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var visible = await access.LoadAsync(taskId, track: true, cancellationToken);
        if (visible is null)
        {
            return CommonErrors.NotFound("Task");
        }

        if (!visible.IsAssignee || !visible.Allows(PermissionCatalog.TasksWork))
        {
            return NotAssignee;
        }

        return visible.Project.IsReadOnly ? Project.ReadOnlyError() : visible;
    }

    private void RecordStatusAudit(TaskItem task, TaskItemStatus previousStatus) =>
        auditLog.Record("task.status_changed", "Task", task.Id.ToString(), new AuditChanges().Change("status", previousStatus, task.Status), task.ProjectId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Work session {SessionId} ended before it started; the end was clamped to the start (clock correction).")]
    private static partial void LogClockMovedBack(ILogger logger, Guid sessionId);
}
