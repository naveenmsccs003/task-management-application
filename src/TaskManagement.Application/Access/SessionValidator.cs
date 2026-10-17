using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TaskManagement.Application.Common;

namespace TaskManagement.Application.Access;

/// <summary>
/// Checks on every authenticated request that the member is still active and that their security stamp
/// (changed by password changes, role changes and deactivation) still matches the one in their cookie
/// (FR-AUTH-08, FR-AUTH-11). Cached briefly; changes on this server evict the cache immediately.
/// </summary>
public sealed class SessionValidator(IAppDbContext db, IMemoryCache cache)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

    private sealed record SessionState(bool IsActive, string SecurityStamp);

    public async Task<bool> IsValidAsync(Guid userId, string? securityStamp, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(securityStamp))
        {
            return false;
        }

        var state = await cache.GetOrCreateAsync(CacheKey(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new SessionState(u.IsActive, u.SecurityStamp))
                .FirstOrDefaultAsync(cancellationToken);
        });

        return state is { IsActive: true } && string.Equals(state.SecurityStamp, securityStamp, StringComparison.Ordinal);
    }

    public void Evict(Guid userId) => cache.Remove(CacheKey(userId));

    private static string CacheKey(Guid userId) => $"session:{userId}";
}
