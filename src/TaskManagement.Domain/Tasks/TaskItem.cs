using TaskManagement.Domain.Common;

namespace TaskManagement.Domain.Tasks;

/// <summary>Which allocated-hour notification is due (FR-TIME-06/07).</summary>
public enum AllocationAlert
{
    Warning,
    Reached,
}

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

    /// <summary>The currently valid approved algorithm submission (FR-ALG-01).</summary>
    public Guid? AlgorithmApprovedSubmissionId { get; private set; }

    /// <summary>Set when a task is created from a checklist template (the items themselves are copied, FR-CHK-02).</summary>
    public Guid? ChecklistTemplateId { get; private set; }

    public List<string> Labels { get; private set; }

    public int CurrentRevision { get; private set; }

    /// <summary>The revision the TL approved; closing needs it to be the current one (FR-RVW-04).</summary>
    public int? ApprovedRevision { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public Guid? ClosedBy { get; private set; }

    /// <summary>Past the due date (in the assignee's time zone) and still open; kept by the due-date job (FR-DL-05).</summary>
    public bool IsOverdue { get; private set; }

    /// <summary>The linked GitLab issue was closed there (FR-IMP-07). Only a flag: GitLab never changes the task's state.</summary>
    public bool GitLabIssueClosed { get; private set; }

    /// <summary>The day the last overdue notification was sent (once per day while overdue).</summary>
    public DateOnly? OverdueNotifiedOn { get; private set; }

    /// <summary>The due date the "due soon" notification was sent for; a new due date can notify again.</summary>
    public DateOnly? DueSoonNotifiedFor { get; private set; }

    /// <summary>Revision for which the allocated-hour warning was sent (once per revision, WQ2).</summary>
    public int? WarnNotifiedRevision { get; private set; }

    /// <summary>Revision for which the "allocated hours reached" notification was sent (FR-TIME-07).</summary>
    public int? LimitNotifiedRevision { get; private set; }

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
        IsOverdue = false;
        CancelledAt = now;
        CancelledBy = by;
        RecordStatus(Status, TaskItemStatus.Cancelled, TaskAction.Cancel, by, CancelReason, now);
        Status = TaskItemStatus.Cancelled;
        Touch(by, now);
        return Result.Success();
    }

    public void RecordChecklistTemplate(Guid templateId) => ChecklistTemplateId = templateId;

    /// <summary>
    /// Decides whether an allocated-hour notification is due for the time spent so far, and records it so it is sent once
    /// per revision. Reaching the limit also counts as the warning, so a member is never warned after the limit message.
    /// Unestimated tasks never notify (edge case 16).
    /// </summary>
    public AllocationAlert? CheckAllocation(long spentSeconds, int warningPercent)
    {
        if (AllocatedMinutes is not { } minutes || minutes <= 0)
        {
            return null;
        }

        var limit = minutes * 60L;
        if (spentSeconds >= limit && LimitNotifiedRevision != CurrentRevision)
        {
            LimitNotifiedRevision = CurrentRevision;
            WarnNotifiedRevision = CurrentRevision;
            return AllocationAlert.Reached;
        }

        if (spentSeconds * 100 >= limit * warningPercent && spentSeconds < limit && WarnNotifiedRevision != CurrentRevision)
        {
            WarnNotifiedRevision = CurrentRevision;
            return AllocationAlert.Warning;
        }

        return null;
    }

    /// <summary>T3: the assignee submits an algorithm (the submission itself is a separate, immutable record).</summary>
    public Result SubmitAlgorithm(Guid by, DateTimeOffset now) => Transition(TaskAction.SubmitAlgorithm, TaskItemStatus.AlgorithmSubmitted, by, null, now);

    /// <summary>T4: the algorithm is approved; the task is ready to start.</summary>
    public Result ApproveAlgorithm(Guid submissionId, Guid by, string? comment, DateTimeOffset now)
    {
        var result = Transition(TaskAction.ApproveAlgorithm, TaskItemStatus.ReadyToStart, by, comment, now);
        if (result.IsSuccess)
        {
            AlgorithmApprovedSubmissionId = submissionId;
        }

        return result;
    }

    /// <summary>T5: rejected with a comment; the assignee submits a new version.</summary>
    public Result RejectAlgorithm(Guid by, string comment, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(comment);
        return Transition(TaskAction.RejectAlgorithm, TaskItemStatus.AwaitingAlgorithm, by, comment, now);
    }

    /// <summary>
    /// T6: start (or restart) work. A task still waiting for its algorithm gets a specific reason (AC-ALG-1) instead of
    /// the generic transition error.
    /// </summary>
    public Result Start(Guid by, DateTimeOffset now)
    {
        if (Status is TaskItemStatus.AwaitingAlgorithm or TaskItemStatus.AlgorithmSubmitted)
        {
            return Error.Conflict("task.algorithm_approval_required", $"Task {Key} needs an approved algorithm before work can start.");
        }

        var result = Transition(TaskAction.Start, TaskItemStatus.InProgress, by, null, now);
        if (result.IsSuccess)
        {
            FirstStartedAt ??= now;
        }

        return result;
    }

    /// <summary>T7: short break; the caller closes the active session.</summary>
    public Result Pause(Guid by, string? reason, DateTimeOffset now) =>
        Transition(TaskAction.Pause, TaskItemStatus.Paused, by, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), now);

    /// <summary>T8: back to work after a pause.</summary>
    public Result Resume(Guid by, DateTimeOffset now) => Transition(TaskAction.Resume, TaskItemStatus.InProgress, by, null, now);

    /// <summary>T9: work halted (end of day, blocked); a reason is required.</summary>
    public Result Stop(Guid by, string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return Transition(TaskAction.Stop, TaskItemStatus.Stopped, by, reason.Trim(), now);
    }

    /// <summary>
    /// The system stops work it had to interrupt (session auto-closed, member deactivated). Only an In progress task has a
    /// session to interrupt; other states are left alone.
    /// </summary>
    public void StopBySystem(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Status != TaskItemStatus.InProgress)
        {
            return;
        }

        RecordStatus(Status, TaskItemStatus.Stopped, TaskAction.Stop, null, reason, now);
        Status = TaskItemStatus.Stopped;
        UpdatedAt = now;
    }

    /// <summary>T10: the assignee submits the current revision for review (the caller has checked the checklist).</summary>
    public Result SubmitReview(Guid by, DateTimeOffset now) => Transition(TaskAction.SubmitReview, TaskItemStatus.InReview, by, null, now);

    /// <summary>
    /// T11: the TL approves the current revision. With the project's "close on approval" option the task closes at once (Q2).
    /// </summary>
    public Result ApproveReview(Guid by, string? comment, bool closeOnApproval, DateTimeOffset now)
    {
        var result = Transition(TaskAction.ApproveReview, TaskItemStatus.Approved, by, comment, now);
        if (result.IsFailure)
        {
            return result;
        }

        ApprovedRevision = CurrentRevision;
        IsOverdue = false;
        return closeOnApproval ? Close(by, "Closed on approval (project setting).", now) : Result.Success();
    }

    /// <summary>
    /// T12: the TL sends the task back. The revision number goes up (FR-REV-01) and the allocated-hour notifications are
    /// judged afresh (WQ2). With "algorithm re-approval required" the task waits for a new algorithm (FR-ALG-06).
    /// </summary>
    public Result RequestRevision(Guid by, string comment, bool algorithmReapprovalRequired, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(comment);
        if (!TaskWorkflow.IsAllowed(Status, TaskAction.RequestRevision))
        {
            return TaskWorkflow.InvalidTransition(Key, Status, TaskAction.RequestRevision);
        }

        StartNewRevision();
        var target = TaskItemStatus.RevisionRequested;
        if (algorithmReapprovalRequired)
        {
            AlgorithmRequired = true;
            AlgorithmApprovedSubmissionId = null;
            target = TaskItemStatus.AwaitingAlgorithm;
        }

        RecordStatus(Status, target, TaskAction.RequestRevision, by, comment.Trim(), now);
        Status = target;
        Touch(by, now);
        return Result.Success();
    }

    /// <summary>T13: final closure, only after the TL approved the current revision (FR-RVW-04, AC-RVW-3).</summary>
    public Result Close(Guid by, string? comment, DateTimeOffset now)
    {
        if (Status == TaskItemStatus.Approved && ApprovedRevision != CurrentRevision)
        {
            return Error.Conflict("task.not_approved", $"Revision {CurrentRevision} of {Key} has not been approved yet.");
        }

        var result = Transition(TaskAction.Close, TaskItemStatus.Closed, by, comment, now);
        if (result.IsSuccess)
        {
            ClosedAt = now;
            ClosedBy = by;
            IsOverdue = false;
        }

        return result;
    }

    /// <summary>T14: reopen a closed task with a reason; it starts a new revision (FR-RVW-05).</summary>
    public Result Reopen(Guid by, string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (!TaskWorkflow.IsAllowed(Status, TaskAction.Reopen))
        {
            return TaskWorkflow.InvalidTransition(Key, Status, TaskAction.Reopen);
        }

        StartNewRevision();
        RecordStatus(Status, TaskItemStatus.RevisionRequested, TaskAction.Reopen, by, reason.Trim(), now);
        Status = TaskItemStatus.RevisionRequested;
        ClosedAt = null;
        ClosedBy = null;
        Touch(by, now);
        return Result.Success();
    }

    /// <summary>
    /// Applies an approved reassignment (task-workflow.md §5). The new state: Awaiting algorithm when re-approval is required
    /// or a required algorithm was never approved; Ready to start if work never started; otherwise Stopped (the new assignee
    /// presses Start). The caller closes the old assignee's session.
    /// </summary>
    public Result Reassign(Guid newAssigneeId, string reason, bool algorithmReapprovalRequired, Guid approvedBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (!TaskWorkflow.CanReassign(Status))
        {
            return Error.Conflict("task.cannot_reassign", $"Task {Key} is '{TaskWorkflow.Describe(Status)}' and cannot be reassigned now.");
        }

        if (algorithmReapprovalRequired && AlgorithmRequired)
        {
            AlgorithmApprovedSubmissionId = null;
        }

        var target = AlgorithmRequired && AlgorithmApprovedSubmissionId is null
            ? TaskItemStatus.AwaitingAlgorithm
            : FirstStartedAt is null ? TaskItemStatus.ReadyToStart : TaskItemStatus.Stopped;
        _assignments.Add(TaskAssignment.Create(Id, AssigneeId, newAssigneeId, AssignmentKind.Reassignment, reason.Trim(), approvedBy, now));
        RecordStatus(Status, target, TaskAction.Reassign, approvedBy, reason.Trim(), now);
        AssigneeId = newAssigneeId;
        Status = target;
        Touch(approvedBy, now);
        return Result.Success();
    }

    /// <summary>An approved extension moves the due date; the original stays in <see cref="OriginalDueDate"/> (FR-DL-03).</summary>
    public void ExtendDueDate(DateOnly newDueDate, Guid approvedBy, DateTimeOffset now)
    {
        DueDate = newDueDate;
        Touch(approvedBy, now);
    }

    /// <summary>Sets or clears the "GitLab issue closed" flag. Returns true when it changed.</summary>
    public bool SetGitLabIssueClosed(bool closed)
    {
        if (GitLabIssueClosed == closed)
        {
            return false;
        }

        GitLabIssueClosed = closed;
        return true;
    }

    /// <summary>Sets the Overdue flag for <paramref name="today"/> (the assignee's local date). Returns true when it changed.</summary>
    public bool RefreshOverdue(DateOnly today)
    {
        var overdue = DueDate is { } due && due < today && TaskWorkflow.IsOpen(Status);
        if (overdue == IsOverdue)
        {
            return false;
        }

        IsOverdue = overdue;
        return true;
    }

    /// <summary>True (and recorded) when today's overdue notification is due: once per day while overdue.</summary>
    public bool TakeOverdueNotification(DateOnly today)
    {
        if (!IsOverdue || OverdueNotifiedOn == today)
        {
            return false;
        }

        OverdueNotifiedOn = today;
        return true;
    }

    /// <summary>True (and recorded) once per due date when it is today or tomorrow and the task is still open.</summary>
    public bool TakeDueSoonNotification(DateOnly today)
    {
        if (DueDate is not { } due || !TaskWorkflow.IsOpen(Status) || IsOverdue || due < today || due > today.AddDays(1) || DueSoonNotifiedFor == due)
        {
            return false;
        }

        DueSoonNotifiedFor = due;
        return true;
    }

    private void StartNewRevision()
    {
        CurrentRevision++;
        WarnNotifiedRevision = null;
        LimitNotifiedRevision = null;
    }

    private Result Transition(TaskAction action, TaskItemStatus target, Guid by, string? comment, DateTimeOffset now)
    {
        if (!TaskWorkflow.IsAllowed(Status, action))
        {
            return TaskWorkflow.InvalidTransition(Key, Status, action);
        }

        RecordStatus(Status, target, action, by, comment, now);
        Status = target;
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
        if (details.AllocatedMinutes != AllocatedMinutes)
        {
            // WQ2: a new allocation is judged afresh, so an already-exceeded task can notify again.
            WarnNotifiedRevision = null;
            LimitNotifiedRevision = null;
        }

        AllocatedMinutes = details.AllocatedMinutes;
        AlgorithmRequired = details.AlgorithmRequired;
        Labels = NormalizeLabels(details.Labels);
        Touch(by, now);
    }

    private void RecordStatus(TaskItemStatus? from, TaskItemStatus to, TaskAction action, Guid? by, string? comment, DateTimeOffset now) =>
        _statusHistory.Add(TaskStatusChange.Create(Id, CurrentRevision, from, to, action, by, comment, now));

    private void Touch(Guid by, DateTimeOffset now)
    {
        UpdatedAt = now;
        UpdatedBy = by;
    }
}
