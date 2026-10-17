using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Application.Tasks;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Common;
using TaskManagement.Domain.Projects;
using TaskManagement.Domain.Time;

namespace TaskManagement.Application.Time;

public sealed record WorkSessionDto(
    Guid Id, UserRef? User, int Revision, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, SessionEndReason? EndReason, long Seconds);

public sealed record TimeAdjustmentDto(Guid Id, UserRef? User, int Revision, int Minutes, string Reason, UserRef? CreatedBy, DateTimeOffset CreatedAt);

public sealed record RevisionTimeDto(int Revision, long Seconds);

public sealed record MemberTimeDto(UserRef? User, long Seconds);

/// <summary>
/// GET /tasks/{id}/time (FR-TIME-05, FR-TIME-08): total = closed sessions + the active session so far + adjustments.
/// Time over the allocation is reported as overrun; work is never blocked by it (Q5).
/// </summary>
public sealed record TaskTimeDto(
    int? AllocatedMinutes,
    long SpentSeconds,
    long OverrunSeconds,
    int? PercentOfAllocation,
    WorkSessionDto? ActiveSession,
    IReadOnlyList<RevisionTimeDto> ByRevision,
    IReadOnlyList<MemberTimeDto> ByMember,
    IReadOnlyList<WorkSessionDto> Sessions,
    IReadOnlyList<TimeAdjustmentDto> Adjustments,
    DateTimeOffset ServerTime);

public sealed record AddTimeAdjustmentCommand(Guid UserId, int Minutes, string? Reason);

/// <summary>Time spent on tasks and corrections to it (FR-TIME-04, FR-TIME-05).</summary>
public sealed class TimeService(IAppDbContext db, TaskAccess access, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<TaskTimeDto>> TaskTimeAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var visible = await access.LoadAsync(taskId, track: false, cancellationToken);
        if (visible is null)
        {
            return CommonErrors.NotFound("Task");
        }

        if (!visible.Allows(PermissionCatalog.TimeView))
        {
            return CommonErrors.Forbidden("view time on this task");
        }

        var now = timeProvider.GetUtcNow();
        var sessions = await db.WorkSessions.AsNoTracking().Where(s => s.TaskId == taskId).OrderBy(s => s.StartedAt).ToListAsync(cancellationToken);
        var adjustments = await db.TimeAdjustments.AsNoTracking().Where(a => a.TaskId == taskId).OrderBy(a => a.CreatedAt).ToListAsync(cancellationToken);
        var userIds = sessions.Select(s => s.UserId).Concat(adjustments.SelectMany(a => new[] { a.UserId, a.CreatedBy })).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new UserRef(u.Id, u.FullName), cancellationToken);

        var sessionDtos = sessions
            .Select(s => new WorkSessionDto(s.Id, names.GetValueOrDefault(s.UserId), s.Revision, s.StartedAt, s.EndedAt, s.EndReason, s.ElapsedSeconds(now)))
            .ToList();
        var entries = sessions.Select(s => (s.UserId, s.Revision, Seconds: s.ElapsedSeconds(now)))
            .Concat(adjustments.Select(a => (a.UserId, a.Revision, Seconds: a.Minutes * 60L)))
            .ToList();
        var spent = Math.Max(0, entries.Sum(e => e.Seconds));
        var allocated = visible.Task.AllocatedMinutes;
        var allocatedSeconds = allocated * 60L;

        return new TaskTimeDto(
            allocated,
            spent,
            allocatedSeconds is { } limit ? Math.Max(0, spent - limit) : 0,
            allocatedSeconds is { } total ? (int)(spent * 100 / total) : null,
            sessionDtos.FirstOrDefault(s => s.EndedAt is null),
            entries.GroupBy(e => e.Revision).OrderBy(g => g.Key).Select(g => new RevisionTimeDto(g.Key, g.Sum(e => e.Seconds))).ToList(),
            entries.GroupBy(e => e.UserId).Select(g => new MemberTimeDto(names.GetValueOrDefault(g.Key), g.Sum(e => e.Seconds))).ToList(),
            sessionDtos,
            adjustments.Select(a => new TimeAdjustmentDto(
                a.Id, names.GetValueOrDefault(a.UserId), a.Revision, a.Minutes, a.Reason, names.GetValueOrDefault(a.CreatedBy), a.CreatedAt)).ToList(),
            now);
    }

    /// <summary>Total seconds spent on a task (used for the task view and the allocated-hour check).</summary>
    public async Task<long> SpentSecondsAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var closed = await db.WorkSessions.Where(s => s.TaskId == taskId && s.DurationSeconds != null).SumAsync(s => (long?)s.DurationSeconds, cancellationToken) ?? 0;
        var activeStart = await db.WorkSessions.Where(s => s.TaskId == taskId && s.EndedAt == null).Select(s => (DateTimeOffset?)s.StartedAt).FirstOrDefaultAsync(cancellationToken);
        var adjusted = await db.TimeAdjustments.Where(a => a.TaskId == taskId).SumAsync(a => (long?)a.Minutes, cancellationToken) ?? 0;
        var active = activeStart is { } start ? Math.Max(0, (long)(now - start).TotalSeconds) : 0;
        return Math.Max(0, closed + active + (adjusted * 60));
    }

    /// <summary>
    /// TL/PM correct a member's time with a reason (FR-TIME-04). Only people who worked on, or were assigned to, the task
    /// can have time adjusted.
    /// </summary>
    public async Task<Result<TaskTimeDto>> AddAdjustmentAsync(Guid taskId, AddTimeAdjustmentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var visible = await access.LoadAsync(taskId, track: false, cancellationToken);
        if (visible is null)
        {
            return CommonErrors.NotFound("Task");
        }

        if (!visible.Permissions.HasProjectWide(PermissionCatalog.TimeAdjust))
        {
            return CommonErrors.Forbidden("adjust time on this task");
        }

        if (visible.Project.IsReadOnly)
        {
            return Project.ReadOnlyError();
        }

        var errors = new FieldErrors()
            .Required("reason", command.Reason, "A reason")
            .MaxLength("reason", command.Reason?.Trim(), TimeAdjustment.ReasonMaxLength, "The reason");
        if (command.Minutes == 0 || Math.Abs(command.Minutes) > TimeAdjustment.MaxMinutes)
        {
            errors.Add("minutes", $"Enter a non-zero number of minutes, at most {TimeAdjustment.MaxMinutes} either way.");
        }

        var workedOnTask = await db.WorkSessions.AnyAsync(s => s.TaskId == taskId && s.UserId == command.UserId, cancellationToken)
            || await db.TaskAssignments.AnyAsync(a => a.TaskId == taskId && a.ToUserId == command.UserId, cancellationToken);
        if (!workedOnTask)
        {
            errors.Add("userId", "Choose someone who worked on or was assigned to this task.");
        }

        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var adjustment = TimeAdjustment.Create(
            taskId, command.UserId, visible.Task.CurrentRevision, command.Minutes, command.Reason!, visible.UserId, timeProvider.GetUtcNow());
        db.TimeAdjustments.Add(adjustment);
        auditLog.Record("time.adjusted", "TimeAdjustment", adjustment.Id.ToString(), new AuditChanges()
            .Set("taskId", taskId)
            .Set("userId", command.UserId)
            .Set("minutes", adjustment.Minutes)
            .Set("reason", adjustment.Reason), visible.Task.ProjectId);
        await db.SaveChangesAsync(cancellationToken);
        return await TaskTimeAsync(taskId, cancellationToken);
    }
}
