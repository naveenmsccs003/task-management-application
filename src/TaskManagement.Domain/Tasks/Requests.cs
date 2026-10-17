using TaskManagement.Domain.Common;

namespace TaskManagement.Domain.Tasks;

public enum ApprovalRequestType
{
    Reassignment,
    DeadlineExtension,
}

public enum ApprovalRequestStatus
{
    Pending,
    Approved,
    Rejected,
    Withdrawn,
    AutoCancelled,
}

/// <summary>
/// A reassignment (FR-REV-02..06) or deadline extension (FR-DL-01..04) waiting for, or past, an approver's decision
/// (task-workflow.md §5–6). The requester never decides their own request; one pending request of each type per task is
/// enforced by a unique partial index.
/// </summary>
public sealed class ApprovalRequest
{
    public const int ReasonMinLength = 10;
    public const int ReasonMaxLength = 1_000;
    public const int CommentMaxLength = 1_000;

    private ApprovalRequest()
    {
        Reason = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid TaskId { get; private set; }

    public ApprovalRequestType Type { get; private set; }

    public ApprovalRequestStatus Status { get; private set; }

    public string Reason { get; private set; }

    public Guid RequestedBy { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>Reassignment: the proposed new assignee.</summary>
    public Guid? ProposedAssigneeId { get; private set; }

    /// <summary>Reassignment: the approved algorithm must be approved again (FR-REV-05).</summary>
    public bool AlgorithmReapprovalRequired { get; private set; }

    /// <summary>Extension: the due date when the request was made.</summary>
    public DateOnly? CurrentDueDate { get; private set; }

    public DateOnly? ProposedDueDate { get; private set; }

    /// <summary>Extension: the date the approver granted (the proposed one or another, FR-DL-02).</summary>
    public DateOnly? ApprovedDueDate { get; private set; }

    public Guid? DecidedBy { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public string? DecisionComment { get; private set; }

    public uint Version { get; private set; }

    public bool IsPending => Status == ApprovalRequestStatus.Pending;

    public static ApprovalRequest Reassignment(Guid taskId, Guid proposedAssigneeId, bool algorithmReapprovalRequired, string reason, Guid requestedBy, DateTimeOffset now) =>
        Create(taskId, ApprovalRequestType.Reassignment, reason, requestedBy, now, r =>
        {
            r.ProposedAssigneeId = proposedAssigneeId;
            r.AlgorithmReapprovalRequired = algorithmReapprovalRequired;
        });

    public static ApprovalRequest DeadlineExtension(Guid taskId, DateOnly currentDueDate, DateOnly proposedDueDate, string reason, Guid requestedBy, DateTimeOffset now) =>
        Create(taskId, ApprovalRequestType.DeadlineExtension, reason, requestedBy, now, r =>
        {
            r.CurrentDueDate = currentDueDate;
            r.ProposedDueDate = proposedDueDate;
        });

    /// <summary>Approves; for an extension <paramref name="approvedDueDate"/> may differ from the proposed date.</summary>
    public Result Approve(Guid by, string? comment, DateOnly? approvedDueDate, DateTimeOffset now)
    {
        var check = CanDecide(by);
        if (check.IsFailure)
        {
            return check;
        }

        if (Type == ApprovalRequestType.DeadlineExtension)
        {
            ApprovedDueDate = approvedDueDate ?? ProposedDueDate;
        }

        return Decide(ApprovalRequestStatus.Approved, by, comment, now);
    }

    public Result Reject(Guid by, string comment, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(comment);
        var check = CanDecide(by);
        return check.IsFailure ? check : Decide(ApprovalRequestStatus.Rejected, by, comment, now);
    }

    public Result Withdraw(Guid by, DateTimeOffset now)
    {
        if (!IsPending)
        {
            return NotPending();
        }

        if (by != RequestedBy)
        {
            return Error.Forbidden("request.not_requester", "Only the person who made the request can withdraw it.");
        }

        return Decide(ApprovalRequestStatus.Withdrawn, by, null, now);
    }

    /// <summary>The task was closed or cancelled, or its project archived (edge cases 26–27).</summary>
    public void AutoCancel(string reason, DateTimeOffset now)
    {
        if (IsPending)
        {
            Decide(ApprovalRequestStatus.AutoCancelled, null, reason, now);
        }
    }

    private static ApprovalRequest Create(Guid taskId, ApprovalRequestType type, string reason, Guid requestedBy, DateTimeOffset now, Action<ApprovalRequest> details)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var request = new ApprovalRequest
        {
            Id = Guid.CreateVersion7(now),
            TaskId = taskId,
            Type = type,
            Status = ApprovalRequestStatus.Pending,
            Reason = reason.Trim(),
            RequestedBy = requestedBy,
            RequestedAt = now,
        };
        details(request);
        return request;
    }

    private Result CanDecide(Guid by)
    {
        if (!IsPending)
        {
            return NotPending();
        }

        return by == RequestedBy
            ? Error.Forbidden("request.own_request", "You cannot decide on your own request.")
            : Result.Success();
    }

    private Result Decide(ApprovalRequestStatus status, Guid? by, string? comment, DateTimeOffset now)
    {
        Status = status;
        DecidedBy = by;
        DecidedAt = now;
        DecisionComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        return Result.Success();
    }

    private Error NotPending() =>
        Error.Conflict("request.not_pending", $"This request is already {Status.ToString().ToLowerInvariant()}.");
}

public enum DelayCategory
{
    BlockedByDependency,
    UnclearRequirement,
    TechnicalIssue,
    Leave,
    Other,
}

/// <summary>The assignee explains a delay (FR-DL-06). It changes neither the state nor the due date.</summary>
public sealed class DelayReport
{
    public const int DescriptionMaxLength = 2_000;

    private DelayReport()
    {
        Description = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid TaskId { get; private set; }

    public int Revision { get; private set; }

    public DelayCategory Category { get; private set; }

    public string Description { get; private set; }

    public Guid ReportedBy { get; private set; }

    public DateTimeOffset ReportedAt { get; private set; }

    public static DelayReport Create(Guid taskId, int revision, DelayCategory category, string description, Guid reportedBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        return new DelayReport
        {
            Id = Guid.CreateVersion7(now),
            TaskId = taskId,
            Revision = revision,
            Category = category,
            Description = description.Trim(),
            ReportedBy = reportedBy,
            ReportedAt = now,
        };
    }
}

public enum ReviewDecision
{
    Pending,
    Approved,
    RevisionRequested,
}

/// <summary>One submission for review of one revision (FR-RVW-01/02) and the TL's decision on it.</summary>
public sealed class ReviewSubmission
{
    public const int SummaryMaxLength = 10_000;
    public const int CommentMaxLength = 2_000;

    private ReviewSubmission()
    {
        Summary = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid TaskId { get; private set; }

    public int Revision { get; private set; }

    public string Summary { get; private set; }

    public Guid SubmittedBy { get; private set; }

    public DateTimeOffset SubmittedAt { get; private set; }

    public ReviewDecision Decision { get; private set; }

    public Guid? ReviewedBy { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    public string? Comment { get; private set; }

    public bool AlgorithmReapprovalRequired { get; private set; }

    public static ReviewSubmission Create(Guid taskId, int revision, string summary, Guid submittedBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        return new ReviewSubmission
        {
            Id = Guid.CreateVersion7(now),
            TaskId = taskId,
            Revision = revision,
            Summary = summary.Trim(),
            SubmittedBy = submittedBy,
            SubmittedAt = now,
            Decision = ReviewDecision.Pending,
        };
    }

    public void Approve(Guid by, string? comment, DateTimeOffset now) => Decide(ReviewDecision.Approved, by, comment, false, now);

    public void RequestRevision(Guid by, string comment, bool algorithmReapprovalRequired, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(comment);
        Decide(ReviewDecision.RevisionRequested, by, comment, algorithmReapprovalRequired, now);
    }

    private void Decide(ReviewDecision decision, Guid by, string? comment, bool reapproval, DateTimeOffset now)
    {
        if (Decision != ReviewDecision.Pending)
        {
            throw new InvalidOperationException("This review was already decided.");
        }

        Decision = decision;
        ReviewedBy = by;
        ReviewedAt = now;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        AlgorithmReapprovalRequired = reapproval;
    }
}
