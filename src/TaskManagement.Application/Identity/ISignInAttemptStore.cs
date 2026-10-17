namespace TaskManagement.Application.Identity;

/// <summary>
/// Records sign-in outcomes with single atomic SQL updates, so parallel guesses cannot race past the
/// lockout counter (FR-AUTH-05).
/// </summary>
public interface ISignInAttemptStore
{
    /// <summary>Counts a failure; returns the lockout end if this failure locked the account.</summary>
    Task<DateTimeOffset?> RecordFailureAsync(Guid userId, int maxFailedAttempts, DateTimeOffset lockoutEndsAt, CancellationToken cancellationToken);

    Task RecordSuccessAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);
}
