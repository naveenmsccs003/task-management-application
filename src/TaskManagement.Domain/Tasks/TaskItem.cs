using TaskManagement.Domain.Common;

namespace TaskManagement.Domain.Tasks;

/// <summary>The editable fields of a task (FR-TASK-03), validated by the application layer before they get here.</summary>
public sealed record TaskDetails(
    string Title,
    string? Description,
    TaskType Type,
    TaskPriority Priority,
    DateOnly? StartDate,
    DateOnly? DueDate,
    int? AllocatedMinutes,
    bool AlgorithmRequired,
    IReadOnlyList<string> Labels);

/// <summary>
/// A unit of work in exactly one project (FR-TASK-01). State changes happen only through the workflow methods
/// (FR-TASK-05); each appends to <see cref="StatusHistory"/> and, for assignments, <see cref="Assignments"/>.
/// Those collections are append-only and are not loaded with the task: they hold only the rows added in this unit of work.
/// Named <c>TaskItem</c> to avoid clashing with System.Threading.Tasks.Task.
/// </summary>
public sealed class TaskItem
{
    public const int TitleMaxLength = 300;
    public const int DescriptionMaxLength = 50_000;
    public const int MaxLabels = 10;
    public const int LabelMaxLength = 30;
    public const int ReasonMaxLength = 1_000;

    /// <summary>10 000 hours; anything larger is a typo.</summary>
    public const int AllocatedMinutesMax = 600_000;

    private readonly List<TaskStatusChange> _statusHistory = [];
    private readonly List<TaskAssignment> _assignments = [];

    private TaskItem()
    {
        Key = string.Empty;
        Title = string.Empty;
        Description = string.Empty;
        Labels = [];
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    /// <summary>Per-project sequence number; unique with <see cref="ProjectId"/>.</summary>
    public int Number { get; private set; }

    /// <summary><c>{project key}-{number}</c>, e.g. <c>TMA-42</c>. Stored because project keys never change.</summary>
    public string Key { get; private set; }

    public string Title { get; private set; }

    /// <summary>Markdown; sanitized when rendered.</summary>
    public string Description { get; private set; }

    public TaskType Type { get; private set; }

    public TaskPriority Priority { get; private set; }

    public TaskItemStatus Status { get; private set; }

    public Guid? AssigneeId { get; private set; }

    public DateOnly? StartDate { get; private set; }

    /// <summary>Required before assignment (WQ1). After assignment it changes only through a deadline extension.</summary>
    public DateOnly? DueDate { get; private set; }

    /// <summary>The due date at first assignment, kept when extensions move <see cref="DueDate"/>.</summary>
    public DateOnly? OriginalDueDate { get; private set; }

    /// <summary>Null means "not estimated" (WQ1).</summary>
    public int? AllocatedMinutes { get; private set; }

    public bool AlgorithmRequired { get; private set; }

    public List<string> Labels { get; private set; }

    public int CurrentRevision { get; private set; }

    /// <summary>When work first started (set by the Start action). The algorithm flag is locked after that (T16).</summary>
    public DateTimeOffset? FirstStartedAt { get; private set; }

    public string? CancelReason { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public Guid? CancelledBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid UpdatedBy { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL <c>xmin</c>, FR-TASK-10).</summary>
    public uint Version { get; private set; }

    public IReadOnlyCollection<TaskStatusChange> StatusHistory => _statusHistory;

    public IReadOnlyCollection<TaskAssignment> Assignments => _assignments;

    public static string FormatKey(string projectKey, int number) => $"{projectKey}-{number}";

    /// <summary>T1: a new Draft task. Revision starts at 0.</summary>
    public static TaskItem Create(Guid projectId, string projectKey, int number, TaskDetails details, Guid createdBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectKey);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        ArgumentException.ThrowIfNullOrWhiteSpace(details.Title);

        var task = new TaskItem
        {
            Id = Guid.CreateVersion7(now),
            ProjectId = projectId,
            Number = number,
            Key = FormatKey(projectKey, number),
            Status = TaskItemStatus.Draft,
            CreatedAt = now,
            CreatedBy = createdBy,
        };
        task.ApplyDetails(details, createdBy, now);
        task.RecordStatus(null, TaskItemStatus.Draft, TaskAction.Create, createdBy, null, now);
        return task;
    }

    /// <summary>
    /// Trims labels, drops blanks and case-insensitive duplicates, and keeps the first spelling and order.
    /// </summary>
    public static List<string> NormalizeLabels(IEnumerable<string?>? labels)
    {
        var result = new List<string>();
        foreach (var label in labels ?? [])
        {
            var trimmed = label?.Trim();
            if (!string.IsNullOrEmpty(trimmed) && !result.Exists(l => string.Equals(l, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(trimmed);
            }
        }

        return result;
    }

    /// <summary>T16: edits fields. The due date is fixed after assignment; the algorithm flag is fixed once work has started.</summary>
    public Result UpdateDetails(TaskDetails details, Guid by, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentException.ThrowIfNullOrWhiteSpace(details.Title);

        if (TaskWorkflow.IsFinal(Status))
        {
            return Error.Conflict("task.read_only", $"Task {Key} is {TaskWorkflow.Describe(Status).ToLowerInvariant()} and can no longer be edited.");
        }

        if (details.DueDate != DueDate && Status != TaskItemStatus.Draft)
        {
            return Error.Conflict(
                "task.due_date_locked",
                "The due date cannot be edited after the task is assigned. Request a deadline extension instead.");
        }

        TaskItemStatus? newStatus = null;
        if (details.AlgorithmRequired != AlgorithmRequired)
        {
            if (FirstStartedAt is not null || Status is not (TaskItemStatus.Draft or TaskItemStatus.AwaitingAlgorithm or TaskItemStatus.ReadyToStart))
            {
                return Error.Conflict(
                    "task.algorithm_flag_locked",
                    "\"Algorithm required\" can only be changed before work starts and while no algorithm is waiting for a decision.");
            }

            newStatus = (Status, details.AlgorithmRequired) switch
            {
                (TaskItemStatus.ReadyToStart, true) => TaskItemStatus.AwaitingAlgorithm,
                (TaskItemStatus.AwaitingAlgorithm, false) => TaskItemStatus.ReadyToStart,
                _ => null,
            };
        }

        ApplyDetails(details, by, now);
        if (newStatus is { } status)
        {
            RecordStatus(Status, status, TaskAction.Update, by, details.AlgorithmRequired ? "Algorithm now required." : "Algorithm no longer required.", now);
            Status = status;
        }

        return Result.Success();
    }

    /// <summary>
    /// T2: first assignment of a Draft task (FR-ASG-03). A due date is required (WQ1); it can be supplied here if the
    /// task does not have one yet. Later changes of assignee are reassignments with approval (FR-ASG-04).
    /// </summary>
    public Result Assign(Guid assigneeId, DateOnly? dueDate, Guid by, DateTimeOffset now)
    {
        if (!TaskWorkflow.IsAllowed(Status, TaskAction.Assign))
        {
            return TaskWorkflow.InvalidTransition(Key, Status, TaskAction.Assign);
        }

        var effectiveDueDate = dueDate ?? DueDate;
        if (effectiveDueDate is null)
        {
            return Error.Validation(
                "task.due_date_required",
                "Set a due date before assigning the task.",
                new Dictionary<string, string[]> { ["dueDate"] = ["A due date is required to assign the task."] });
        }

        var target = AlgorithmRequired ? TaskItemStatus.AwaitingAlgorithm : TaskItemStatus.ReadyToStart;
        DueDate = effectiveDueDate;
        OriginalDueDate = effectiveDueDate;
        AssigneeId = assigneeId;
        _assignments.Add(TaskAssignment.Create(Id, null, assigneeId, AssignmentKind.Initial, null, by, now));
        RecordStatus(Status, target, TaskAction.Assign, by, null, now);
        Status = target;
        Touch(by, now);
        return Result.Success();
    }

    /// <summary>T15: cancels any non-final task with a reason. Cancelled tasks are kept, never deleted (FR-TASK-09).</summary>
    public Result Cancel(string reason, Guid by, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (!TaskWorkflow.IsAllowed(Status, TaskAction.Cancel))
        {
            return TaskWorkflow.InvalidTransition(Key, Status, TaskAction.Cancel);
        }

        CancelReason = reason.Trim();
        CancelledAt = now;
        CancelledBy = by;
        RecordStatus(Status, TaskItemStatus.Cancelled, TaskAction.Cancel, by, CancelReason, now);
        Status = TaskItemStatus.Cancelled;
        Touch(by, now);
        return Result.Success();
    }

    private void ApplyDetails(TaskDetails details, Guid by, DateTimeOffset now)
    {
        Title = details.Title.Trim();
        Description = details.Description?.Trim() ?? string.Empty;
        Type = details.Type;
        Priority = details.Priority;
        StartDate = details.StartDate;
        DueDate = details.DueDate;
        AllocatedMinutes = details.AllocatedMinutes;
        AlgorithmRequired = details.AlgorithmRequired;
        Labels = NormalizeLabels(details.Labels);
        Touch(by, now);
    }

    private void RecordStatus(TaskItemStatus? from, TaskItemStatus to, TaskAction action, Guid by, string? comment, DateTimeOffset now) =>
        _statusHistory.Add(TaskStatusChange.Create(Id, CurrentRevision, from, to, action, by, comment, now));

    private void Touch(Guid by, DateTimeOffset now)
    {
        UpdatedAt = now;
        UpdatedBy = by;
    }
}
