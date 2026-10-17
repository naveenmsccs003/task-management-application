using TaskManagement.Domain.Tasks;

namespace TaskManagement.Domain.Tests.Tasks;

public sealed class AlgorithmSubmissionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Developer = Guid.CreateVersion7();
    private static readonly Guid Lead = Guid.CreateVersion7();

    private static AlgorithmSubmission NewSubmission() => AlgorithmSubmission.Create(Guid.CreateVersion7(), 0, 1, " 1. Validate input ", Developer, Now);

    [Fact]
    public void New_submissions_are_pending_and_trimmed()
    {
        var submission = NewSubmission();

        Assert.Equal(AlgorithmDecision.Pending, submission.Decision);
        Assert.Equal("1. Validate input", submission.Content);
    }

    [Fact]
    public void A_decision_is_recorded_once()
    {
        var submission = NewSubmission();

        var approved = submission.Approve(Lead, "  Looks good ", Now.AddHours(1));
        var again = submission.Reject(Lead, "Changed my mind", Now.AddHours(2));

        Assert.True(approved.IsSuccess);
        Assert.Equal(AlgorithmDecision.Approved, submission.Decision);
        Assert.Equal("Looks good", submission.DecisionComment);
        Assert.Equal(Lead, submission.DecidedBy);
        Assert.Equal("algorithm.already_decided", again.Error!.Code);
    }

    [Fact]
    public void Nobody_decides_on_their_own_submission()
    {
        var submission = NewSubmission();

        var result = submission.Approve(Developer, null, Now);

        Assert.Equal("algorithm.own_submission", result.Error!.Code);
        Assert.Equal(Domain.Common.ErrorType.Forbidden, result.Error.Type);
        Assert.Equal(AlgorithmDecision.Pending, submission.Decision);
    }
}
