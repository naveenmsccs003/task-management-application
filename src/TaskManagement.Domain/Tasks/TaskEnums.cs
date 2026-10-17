namespace TaskManagement.Domain.Tasks;

/// <summary>Task states from docs/task-workflow.md §1. Named <c>TaskItemStatus</c> to avoid clashing with System.Threading.Tasks.TaskStatus.</summary>
public enum TaskItemStatus
{
    Draft,
    AwaitingAlgorithm,
    AlgorithmSubmitted,
    ReadyToStart,
    InProgress,
    Paused,
    Stopped,
    InReview,
    RevisionRequested,
    Approved,
    Closed,
    Cancelled,
}

public enum TaskPriority
{
    Low,
    Medium,
    High,
    Critical,
}

public enum TaskType
{
    Feature,
    Bug,
    Improvement,
    Research,
    Other,
}

/// <summary>
/// Workflow actions T1–T15 (docs/task-workflow.md §3). <see cref="Update"/> (T16, field edits) appears in status
/// history only when changing the "algorithm required" flag moves the task between Awaiting algorithm and Ready to start.
/// </summary>
public enum TaskAction
{
    Create,
    Update,
    Assign,
    SubmitAlgorithm,
    ApproveAlgorithm,
    RejectAlgorithm,
    Start,
    Pause,
    Resume,
    Stop,
    SubmitReview,
    ApproveReview,
    RequestRevision,
    Close,
    Reopen,
    Cancel,
}

public enum AssignmentKind
{
    Initial,
    Reassignment,
}
