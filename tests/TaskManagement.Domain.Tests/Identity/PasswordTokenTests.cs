using TaskManagement.Domain.Identity;

namespace TaskManagement.Domain.Tests.Identity;

public sealed class PasswordTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Hash = new byte[32];

    private static PasswordToken Issue() =>
        PasswordToken.Issue(Guid.NewGuid(), PasswordTokenPurpose.Reset, Hash, TimeSpan.FromMinutes(30), "127.0.0.1", Now);

    [Fact]
    public void Token_is_usable_until_it_expires()
    {
        var token = Issue();

        Assert.True(token.IsUsable(Now.AddMinutes(29)));
        Assert.False(token.IsUsable(Now.AddMinutes(30)));
    }

    [Fact]
    public void Token_is_single_use()
    {
        var token = Issue();

        token.MarkUsed(Now.AddMinutes(1));

        Assert.False(token.IsUsable(Now.AddMinutes(2)));
    }

    [Fact]
    public void Revoked_token_cannot_be_used_and_used_token_is_not_marked_revoked()
    {
        var revoked = Issue();
        revoked.Revoke(Now);
        Assert.False(revoked.IsUsable(Now));

        var used = Issue();
        used.MarkUsed(Now);
        used.Revoke(Now.AddMinutes(1));
        Assert.Null(used.RevokedAt);
    }

    [Fact]
    public void Only_sha256_sized_hashes_are_accepted()
    {
        Assert.Throws<ArgumentException>(() =>
            PasswordToken.Issue(Guid.NewGuid(), PasswordTokenPurpose.Invite, new byte[16], TimeSpan.FromHours(1), null, Now));
    }
}
