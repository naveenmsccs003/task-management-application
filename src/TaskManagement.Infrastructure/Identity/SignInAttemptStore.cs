using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Identity;
using TaskManagement.Infrastructure.Persistence;

namespace TaskManagement.Infrastructure.Identity;

/// <summary>Atomic SQL updates for sign-in counters (see <see cref="ISignInAttemptStore"/>).</summary>
internal sealed class SignInAttemptStore(AppDbContext db) : ISignInAttemptStore
{
    public async Task<DateTimeOffset?> RecordFailureAsync(
        Guid userId, int maxFailedAttempts, DateTimeOffset lockoutEndsAt, CancellationToken cancellationToken)
    {
        // One statement: increment, and when the limit is reached lock the account and reset the counter.
        var result = await db.Database.SqlQuery<DateTimeOffset?>($"""
            UPDATE identity.users
            SET failed_sign_in_count = CASE WHEN failed_sign_in_count + 1 >= {maxFailedAttempts} THEN 0 ELSE failed_sign_in_count + 1 END,
                lockout_ends_at = CASE WHEN failed_sign_in_count + 1 >= {maxFailedAttempts} THEN {lockoutEndsAt} ELSE lockout_ends_at END
            WHERE id = {userId}
            RETURNING CASE WHEN failed_sign_in_count = 0 AND lockout_ends_at = {lockoutEndsAt} THEN lockout_ends_at END AS "Value"
            """).ToListAsync(cancellationToken);
        return result.FirstOrDefault();
    }

    public Task RecordSuccessAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync($"""
            UPDATE identity.users
            SET failed_sign_in_count = 0, lockout_ends_at = NULL, last_sign_in_at = {now}
            WHERE id = {userId}
            """, cancellationToken);
}
