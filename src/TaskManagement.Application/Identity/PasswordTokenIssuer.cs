using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Common;
using TaskManagement.Domain.Identity;

namespace TaskManagement.Application.Identity;

/// <summary>Issues a new password token and revokes every older one, so only the newest link works.</summary>
public sealed class PasswordTokenIssuer(IAppDbContext db, ICurrentUser currentUser, TimeProvider timeProvider)
{
    public async Task<string> IssueAsync(User user, PasswordTokenPurpose purpose, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var now = timeProvider.GetUtcNow();
        await RevokeAllAsync(user.Id, now, cancellationToken);

        var (token, hash) = PasswordTokens.Generate();
        db.PasswordTokens.Add(PasswordToken.Issue(user.Id, purpose, hash, lifetime, currentUser.IpAddress, now));
        return token;
    }

    public async Task RevokeAllAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var open = await db.PasswordTokens
            .Where(t => t.UserId == userId && t.UsedAt == null && t.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in open)
        {
            token.Revoke(now);
        }
    }
}
