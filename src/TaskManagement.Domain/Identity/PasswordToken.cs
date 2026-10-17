namespace TaskManagement.Domain.Identity;

/// <summary>
/// A single-use, expiring password token. Only a SHA-256 hash of the token is stored;
/// the raw token exists only in the email link (FR-AUTH-07).
/// </summary>
public sealed class PasswordToken
{
    private PasswordToken()
    {
        TokenHash = [];
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public PasswordTokenPurpose Purpose { get; private set; }

    public byte[] TokenHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RequestedIp { get; private set; }

    public static PasswordToken Issue(
        Guid userId, PasswordTokenPurpose purpose, byte[] tokenHash, TimeSpan lifetime, string? requestedIp, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);
        if (tokenHash.Length != 32)
        {
            throw new ArgumentException("Expected a SHA-256 hash.", nameof(tokenHash));
        }

        return new PasswordToken
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            Purpose = purpose,
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = now + lifetime,
            RequestedIp = requestedIp,
        };
    }

    public bool IsUsable(DateTimeOffset now) => UsedAt is null && RevokedAt is null && ExpiresAt > now;

    public void MarkUsed(DateTimeOffset now) => UsedAt = now;

    public void Revoke(DateTimeOffset now)
    {
        if (UsedAt is null && RevokedAt is null)
        {
            RevokedAt = now;
        }
    }
}
