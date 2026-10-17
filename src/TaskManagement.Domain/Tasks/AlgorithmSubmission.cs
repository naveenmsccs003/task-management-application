using TaskManagement.Domain.Common;

namespace TaskManagement.Domain.Tasks;

public enum AlgorithmDecision
{
    Pending,
    Approved,
    Rejected,
}

/// <summary>
/// One version of a task's algorithm (FR-ALG-02..04). The content never changes after submission; only the decision is
/// recorded once. Every version stays as history.
/// </summary>
public sealed class AlgorithmSubmission
{
    public const int ContentMaxLength = 50_000;
    public const int CommentMaxLength = 2_000;

    private AlgorithmSubmission()
    {
        Content = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid TaskId { get; private set; }

    public int Revision { get; private set; }

    /// <summary>1, 2, 3 … per task.</summary>
    public int Version { get; private set; }

    /// <summary>Markdown; immutable.</summary>
    public string Content { get; private set; }

    public Guid SubmittedBy { get; private set; }

    public DateTimeOffset SubmittedAt { get; private set; }

    public AlgorithmDecision Decision { get; private set; }

    public Guid? DecidedBy { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public string? DecisionComment { get; private set; }

    public static AlgorithmSubmission Create(Guid taskId, int revision, int version, string content, Guid submittedBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        return new AlgorithmSubmission
        {
            Id = Guid.CreateVersion7(now),
            TaskId = taskId,
            Revision = revision,
            Version = version,
            Content = content.Trim(),
            SubmittedBy = submittedBy,
            SubmittedAt = now,
            Decision = AlgorithmDecision.Pending,
        };
    }

    public Result Approve(Guid by, string? comment, DateTimeOffset now) => Decide(AlgorithmDecision.Approved, by, comment, now);

    public Result Reject(Guid by, string comment, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(comment);
        return Decide(AlgorithmDecision.Rejected, by, comment, now);
    }

    private Result Decide(AlgorithmDecision decision, Guid by, string? comment, DateTimeOffset now)
    {
        if (Decision != AlgorithmDecision.Pending)
        {
            return Error.Conflict("algorithm.already_decided", $"Version {Version} was already {Decision.ToString().ToLowerInvariant()}.");
        }

        // Separation of duties (FR-ALG-05): nobody approves or rejects their own algorithm.
        if (by == SubmittedBy)
        {
            return Error.Forbidden("algorithm.own_submission", "You cannot decide on your own algorithm. Another Team Leader must review it.");
        }

        Decision = decision;
        DecidedBy = by;
        DecidedAt = now;
        DecisionComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        return Result.Success();
    }
}
