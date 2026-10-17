namespace TaskManagement.Domain.Time;

/// <summary>Why a work session ended (FR-TIME-01).</summary>
public enum SessionEndReason
{
    Paused,
    Stopped,
    Submitted,
    AutoClosed,
    Reassigned,
    Cancelled,
    MemberDeactivated,
}

/// <summary>
/// One continuous stretch of work by one member on one task (FR-TIME-01). Times come only from the server clock in UTC
/// (FR-TIME-02). Sessions are never edited or deleted; corrections are <see cref="TimeAdjustment"/>s (FR-TIME-04). The
/// database allows one active session per member and per task (FR-TIME-03).
/// </summary>
public sealed class WorkSession
{
    private WorkSession()
    {
    }

    public Guid Id { get; private set; }

    public Guid TaskId { get; private set; }

    public Guid UserId { get; private set; }

    public int Revision { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>Null while the session is active.</summary>
    public DateTimeOffset? EndedAt { get; private set; }

    public SessionEndReason? EndReason { get; private set; }

    public int? DurationSeconds { get; private set; }

    public bool IsActive => EndedAt is null;

    public static WorkSession Start(Guid taskId, Guid userId, int revision, DateTimeOffset now) =>
        new() { Id = Guid.CreateVersion7(now), TaskId = taskId, UserId = userId, Revision = revision, StartedAt = now };

    /// <summary>
    /// Ends the session once. If the clock moved backwards the end is clamped to the start, so a negative duration is never
    /// stored (edge case 18). Returns false when the end had to be clamped, so the caller can log it.
    /// </summary>
    public bool End(DateTimeOffset now, SessionEndReason reason)
    {
        if (EndedAt is not null)
        {
            throw new InvalidOperationException("The work session has already ended.");
        }

        var clamped = now < StartedAt;
        EndedAt = clamped ? StartedAt : now;
        EndReason = reason;
        DurationSeconds = (int)(EndedAt.Value - StartedAt).TotalSeconds;
        return !clamped;
    }

    /// <summary>Seconds worked so far: the stored duration, or the elapsed time of an active session.</summary>
    public long ElapsedSeconds(DateTimeOffset now) =>
        DurationSeconds ?? Math.Max(0, (long)(now - StartedAt).TotalSeconds);
}

/// <summary>A correction of a member's time on a task, in minutes (positive or negative), with a reason (FR-TIME-04).</summary>
public sealed class TimeAdjustment
{
    public const int ReasonMaxLength = 1_000;

    /// <summary>One week in either direction; anything larger is almost certainly a typo.</summary>
    public const int MaxMinutes = 7 * 24 * 60;

    private TimeAdjustment()
    {
        Reason = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid TaskId { get; private set; }

    /// <summary>Whose time is corrected.</summary>
    public Guid UserId { get; private set; }

    public int Revision { get; private set; }

    public int Minutes { get; private set; }

    public string Reason { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static TimeAdjustment Create(Guid taskId, Guid userId, int revision, int minutes, string reason, Guid createdBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (minutes == 0 || Math.Abs(minutes) > MaxMinutes)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes), minutes, $"Adjustments must be non-zero and at most {MaxMinutes} minutes either way.");
        }

        return new TimeAdjustment
        {
            Id = Guid.CreateVersion7(now),
            TaskId = taskId,
            UserId = userId,
            Revision = revision,
            Minutes = minutes,
            Reason = reason.Trim(),
            CreatedBy = createdBy,
            CreatedAt = now,
        };
    }
}
