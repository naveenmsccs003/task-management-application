using Microsoft.EntityFrameworkCore;
using TaskManagement.Domain.Operations;

namespace TaskManagement.Application.Common;

/// <summary>Stores and replays the first response to a request sent with an <c>Idempotency-Key</c> (FR-WORK-07).</summary>
public sealed class IdempotencyStore(IAppDbContext db, TimeProvider timeProvider)
{
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    public Task<IdempotencyRecord?> FindAsync(Guid userId, Guid key, CancellationToken cancellationToken) =>
        db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == userId && r.Key == key, cancellationToken);

    /// <summary>Saves the response. If a parallel request with the same key saved first, its record wins and nothing changes.</summary>
    public async Task SaveAsync(Guid userId, Guid key, string endpoint, int status, string body, CancellationToken cancellationToken)
    {
        db.DiscardChanges();
        db.IdempotencyRecords.Add(IdempotencyRecord.Create(userId, key, endpoint, status, body, timeProvider.GetUtcNow()));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (db.IsUniqueViolation(exception))
        {
            db.DiscardChanges();
        }
    }

    /// <summary>Removes records older than <see cref="Retention"/>; returns how many.</summary>
    public Task<int> DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow() - Retention;
        return db.IdempotencyRecords.Where(r => r.CreatedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
    }
}
