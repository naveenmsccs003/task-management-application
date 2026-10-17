using TaskManagement.Application.Identity;

namespace TaskManagement.Application.Tests.Identity;

public sealed class PasswordPolicyTests
{
    private const string Email = "ravi.kumar@example.com";

    [Fact]
    public void Long_unusual_password_is_accepted()
    {
        Assert.False(PasswordPolicy.Validate("river-copper-lantern-42", Email, 12).HasErrors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short-pass1")]
    public void Missing_or_too_short_password_is_rejected(string? password)
    {
        Assert.True(PasswordPolicy.Validate(password, Email, 12).HasErrors);
    }

    [Fact]
    public void Minimum_length_comes_from_settings()
    {
        Assert.False(PasswordPolicy.Validate("tiger-moon-7", Email, 12).HasErrors);
        Assert.True(PasswordPolicy.Validate("tiger-moon-7", Email, 16).HasErrors);
    }

    [Fact]
    public void Password_longer_than_128_characters_is_rejected()
    {
        Assert.True(PasswordPolicy.Validate(new string('a', 64) + new string('b', 65), Email, 12).HasErrors);
    }

    [Theory]
    [InlineData("Password1234")]
    [InlineData("PASSWORD1234")]
    [InlineData("qwerty123456")]
    [InlineData("aaaaaaaaaaaaaaaa")]
    [InlineData("abababababababab")]
    public void Common_or_trivial_passwords_are_rejected(string password)
    {
        Assert.True(PasswordPolicy.Validate(password, Email, 12).HasErrors);
    }

    [Fact]
    public void Password_containing_the_email_name_is_rejected()
    {
        Assert.True(PasswordPolicy.Validate("my-RAVI.KUMAR-secret", Email, 12).HasErrors);
    }

    [Fact]
    public void Composition_rules_are_not_required()
    {
        // NIST SP 800-63B: no forced digits/symbols/uppercase.
        Assert.False(PasswordPolicy.Validate("correct horse battery staple", Email, 12).HasErrors);
    }
}
