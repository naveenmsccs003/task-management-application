using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TaskManagement.Application.Common;
using TaskManagement.Domain.Access;

namespace TaskManagement.Application.Access;

/// <summary>
/// Resolves a member's permissions from the database, cached for at most one minute so role and
/// permission changes take effect within a minute on every server (FR-ROLE-07). Changes made on this
/// server take effect immediately because the cache generation is bumped.
/// </summary>
public sealed class PermissionService(IAppDbContext db, IMemoryCache cache)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);
    private static long CacheGeneration;

    public async Task<EffectivePermissions> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync($"permissions:{Interlocked.Read(ref CacheGeneration)}:{userId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            var grants = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId && u.IsActive)
                .SelectMany(u => db.Roles.Where(r => r.Id == u.GlobalRoleId).SelectMany(r => r.Permissions))
                .ToDictionaryAsync(p => p.PermissionCode, p => p.Scope, StringComparer.Ordinal, cancellationToken);
            return new EffectivePermissions(grants);
        }) ?? EffectivePermissions.None;

    /// <summary>Call after any role-permission or role-assignment change.</summary>
    public static void InvalidateAll() => Interlocked.Increment(ref CacheGeneration);
}
