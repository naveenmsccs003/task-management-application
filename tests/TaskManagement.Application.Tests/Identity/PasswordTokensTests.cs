using TaskManagement.Application.Identity;

namespace TaskManagement.Application.Tests.Identity;

public sealed class PasswordTokensTests
{
    [Fact]
    public void Generated_tokens_are_url_safe_random_and_hash_matches()
    {
        var (token, hash) = PasswordTokens.Generate();
        var (other, _) = PasswordTokens.Generate();

        Assert.Equal(43, token.Length); // 32 bytes, base64url without padding
        Assert.Matches("^[A-Za-z0-9_-]+$", token);
        Assert.NotEqual(token, other);
        Assert.Equal(32, hash.Length);
        Assert.Equal(hash, PasswordTokens.Hash(token));
    }
}
