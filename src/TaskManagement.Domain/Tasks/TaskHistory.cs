namespace TaskManagement.Domain.Tasks;

/// <summary>One state change of a task (append-only). Together with assignments it forms the task's timeline.</summary>
public sealed class TaskStatusChange
{
    private TaskStatusChange()
    {
    }

    public Guid Id { get; private set; }

    public Guid TaskId { get; private set; }

    public int Revision { get; private set; }

    /// <summary>Null for the creation entry.</summary>
    public TaskItemStatus? FromStatus { get; private set; }

    public TaskItemStatus ToStatus { get; private set; }

    public TaskAction Action { get; private set; }

    /// <summary>Null when the system made the change (e.g. a session auto-closed by the background job).</summary>
    public Guid? ActorId { get; private set; }

    public string? Comment { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    internal static TaskStatusChange Create(
        Guid taskId, int revision, TaskItemStatus? from, TaskItemStatus to, TaskAction action, Guid? actorId, string? comment, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            TaskId = taskId,
            Revision = revision,
            FromStatus = from,
            ToStatus = to,
            Action = action,
            ActorId = actorId,
            Comment = comment,
            OccurredAt = now,
        };
}

/// <summary>One assignment or reassignment of a task (append-only, FR-ASG-05).</summary>
public sealed class TaskAssignment
{
    private TaskAssignment()
    {
    }

    public Guid Id { get; private set; }

    public Guid TaskId { get; private set; }

    /// <summary>Null for the first assignment.</summary>
    public Guid? FromUserId { get; private set; }

    public Guid ToUserId { get; private set; }

    public AssignmentKind Kind { get; private set; }

    public string? Reason { get; private set; }

    public Guid AssignedBy { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    internal static TaskAssignment Create(
        Guid taskId, Guid? fromUserId, Guid toUserId, AssignmentKind kind, string? reason, Guid assignedBy, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            TaskId = taskId,
            FromUserId = fromUserId,
            ToUserId = toUserId,
            Kind = kind,
            Reason = reason,
            AssignedBy = assignedBy,
            OccurredAt = now,
        };
}
