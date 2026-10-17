using TaskManagement.Domain.Access;
using TaskManagement.Domain.Identity;

namespace TaskManagement.Domain.Tests.Identity;

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    private static User NewUser() =>
        User.Create("  Dev.One@Example.com ", " Dev One ", "Asia/Kolkata", null, SystemRoles.DeveloperId, null, Now);

    [Fact]
    public void Create_trims_input_normalizes_email_and_starts_active_without_password()
    {
        var user = NewUser();

        Assert.Equal("Dev.One@Example.com", user.Email);
        Assert.Equal("DEV.ONE@EXAMPLE.COM", user.NormalizedEmail);
        Assert.Equal("Dev One", user.FullName);
        Assert.True(user.IsActive);
        Assert.Null(user.PasswordHash);
        Assert.False(string.IsNullOrEmpty(user.SecurityStamp));
    }

    [Fact]
    public void Setting_a_password_changes_the_security_stamp_and_clears_lockout()
    {
        var user = NewUser();
        var stamp = user.SecurityStamp;

        user.SetPasswordHash("hash", Now);

        Assert.Equal("hash", user.PasswordHash);
        Assert.NotEqual(stamp, user.SecurityStamp);
        Assert.Null(user.LockoutEndsAt);
        Assert.Equal(Now, user.PasswordChangedAt);
    }

    [Fact]
    public void Rehash_keeps_the_security_stamp()
    {
        var user = NewUser();
        var stamp = user.SecurityStamp;

        user.RehashPassword("new-hash");

        Assert.Equal(stamp, user.SecurityStamp);
    }

    [Fact]
    public void Changing_role_changes_stamp_but_same_role_does_not()
    {
        var user = NewUser();
        var stamp = user.SecurityStamp;

        user.ChangeGlobalRole(SystemRoles.DeveloperId);
        Assert.Equal(stamp, user.SecurityStamp);

        user.ChangeGlobalRole(SystemRoles.TeamLeaderId);
        Assert.NotEqual(stamp, user.SecurityStamp);
        Assert.Equal(SystemRoles.TeamLeaderId, user.GlobalRoleId);
    }

    [Fact]
    public void Deactivate_ends_sessions_and_reactivate_restores_access()
    {
        var user = NewUser();
        var stamp = user.SecurityStamp;
        var admin = Guid.NewGuid();

        user.Deactivate(admin, Now);

        Assert.False(user.IsActive);
        Assert.Equal(admin, user.DeactivatedBy);
        Assert.NotEqual(stamp, user.SecurityStamp);

        user.Reactivate();

        Assert.True(user.IsActive);
        Assert.Null(user.DeactivatedAt);
    }

    [Fact]
    public void Employee_code_blank_is_stored_as_null()
    {
        var user = NewUser();

        user.SetEmployeeCode("   ");

        Assert.Null(user.EmployeeCode);
    }
}
