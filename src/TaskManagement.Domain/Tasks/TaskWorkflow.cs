using TaskManagement.Domain.Common;

namespace TaskManagement.Domain.Tasks;

/// <summary>
/// The task state machine (docs/task-workflow.md §2–3) in one place (FR-TASK-05): which actions each state allows.
/// Permission and assignee checks are separate; this answers only "does the workflow allow it now?".
/// </summary>
public static class TaskWorkflow
{
    private static readonly Dictionary<TaskItemStatus, TaskAction[]> Allowed = new()
    {
        [TaskItemStatus.Draft] = [TaskAction.Assign, TaskAction.Cancel],
        [TaskItemStatus.AwaitingAlgorithm] = [TaskAction.SubmitAlgorithm, TaskAction.Cancel],
        [TaskItemStatus.AlgorithmSubmitted] = [TaskAction.ApproveAlgorithm, TaskAction.RejectAlgorithm, TaskAction.Cancel],
        [TaskItemStatus.ReadyToStart] = [TaskAction.Start, TaskAction.Cancel],
        [TaskItemStatus.InProgress] = [TaskAction.Pause, TaskAction.Stop, TaskAction.SubmitReview, TaskAction.Cancel],
        [TaskItemStatus.Paused] = [TaskAction.Resume, TaskAction.Stop, TaskAction.SubmitReview, TaskAction.Cancel],
        [TaskItemStatus.Stopped] = [TaskAction.Start, TaskAction.SubmitReview, TaskAction.Cancel],
        [TaskItemStatus.InReview] = [TaskAction.ApproveReview, TaskAction.RequestRevision, TaskAction.Cancel],
        [TaskItemStatus.RevisionRequested] = [TaskAction.Start, TaskAction.Cancel],
        [TaskItemStatus.Approved] = [TaskAction.Close, TaskAction.Cancel],
        [TaskItemStatus.Closed] = [TaskAction.Reopen],
        [TaskItemStatus.Cancelled] = [],
    };

    public static IReadOnlyList<TaskAction> AllowedActions(TaskItemStatus status) => Allowed[status];

    public static bool IsAllowed(TaskItemStatus status, TaskAction action) => Array.IndexOf(Allowed[status], action) >= 0;

    /// <summary>States in which a reassignment may be requested and approved (task-workflow.md §5).</summary>
    public static bool CanReassign(TaskItemStatus status) =>
        status is TaskItemStatus.AwaitingAlgorithm or TaskItemStatus.AlgorithmSubmitted or TaskItemStatus.ReadyToStart
            or TaskItemStatus.InProgress or TaskItemStatus.Paused or TaskItemStatus.Stopped or TaskItemStatus.RevisionRequested;

    /// <summary>Closed and Cancelled are final and read-only.</summary>
    public static bool IsFinal(TaskItemStatus status) => status is TaskItemStatus.Closed or TaskItemStatus.Cancelled;

    /// <summary>Open = still needs work or a decision. Used for "open tasks" checks on members and projects.</summary>
    public static bool IsOpen(TaskItemStatus status) =>
        status is not (TaskItemStatus.Approved or TaskItemStatus.Closed or TaskItemStatus.Cancelled);

    /// <summary>States that count as open, as a list for database queries.</summary>
    public static IReadOnlyList<TaskItemStatus> OpenStatuses { get; } =
        Enum.GetValues<TaskItemStatus>().Where(IsOpen).ToArray();

    /// <summary>The 409 returned for an action the current state does not allow (task-workflow.md §3, last paragraph).</summary>
    public static Error InvalidTransition(string taskKey, TaskItemStatus status, TaskAction action)
    {
        var allowed = Allowed[status];
        var list = allowed.Length == 0 ? "none" : string.Join(", ", allowed.Select(ToActionName));
        return Error.Conflict(
            "task.invalid_transition",
            $"Task {taskKey} is '{Describe(status)}', so '{ToActionName(action)}' is not allowed. Allowed actions: {list}.");
    }

    public static string Describe(TaskItemStatus status) => status switch
    {
        TaskItemStatus.AwaitingAlgorithm => "Awaiting algorithm",
        TaskItemStatus.AlgorithmSubmitted => "Algorithm submitted",
        TaskItemStatus.ReadyToStart => "Ready to start",
        TaskItemStatus.InProgress => "In progress",
        TaskItemStatus.InReview => "In review",
        TaskItemStatus.RevisionRequested => "Revision requested",
        _ => status.ToString(),
    };

    /// <summary>The kebab-case name used in URLs and messages, e.g. <c>submit-review</c>.</summary>
    public static string ToActionName(TaskAction action) => action switch
    {
        TaskAction.SubmitAlgorithm => "submit-algorithm",
        TaskAction.ApproveAlgorithm => "approve-algorithm",
        TaskAction.RejectAlgorithm => "reject-algorithm",
        TaskAction.SubmitReview => "submit-review",
        TaskAction.ApproveReview => "approve",
        TaskAction.RequestRevision => "request-revision",
        TaskAction.Reassign => "reassign",
        _ => action.ToString().ToLowerInvariant(),
    };
}
