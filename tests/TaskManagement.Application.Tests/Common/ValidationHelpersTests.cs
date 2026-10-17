using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Application.Settings;
using TaskManagement.Domain.Common;

namespace TaskManagement.Application.Tests.Common;

public sealed class ValidationHelpersTests
{
    [Fact]
    public void Field_errors_become_one_validation_error_grouped_by_field()
    {
        var error = new FieldErrors()
            .Required("name", " ", "Name")
            .Add("name", "Another problem.")
            .MaxLength("code", "abcdef", 3, "Code")
            .ToError();

        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(2, error.FieldErrors!["name"].Length);
        Assert.Single(error.FieldErrors["code"]);
    }

    [Theory]
    [InlineData("user@example.com", true)]
    [InlineData("  user@example.com  ", true)]
    [InlineData("User Name <user@example.com>", false)]
    [InlineData("not-an-email", false)]
    [InlineData("", false)]
    public void Email_validation_accepts_plain_addresses_only(string email, bool valid)
    {
        Assert.Equal(valid, InputRules.IsValidEmail(email));
    }

    [Theory]
    [InlineData("Asia/Kolkata", true)]
    [InlineData("UTC", true)]
    [InlineData("Mars/Olympus_Mons", false)]
    [InlineData("", false)]
    public void Time_zone_validation_uses_iana_ids(string timeZone, bool valid)
    {
        Assert.Equal(valid, InputRules.IsValidTimeZone(timeZone));
    }

    [Fact]
    public void Auth_settings_defaults_are_valid_and_out_of_range_values_are_rejected()
    {
        Assert.False(new AuthSettings().Validate().HasErrors);

        var errors = new AuthSettings { PasswordMinLength = 4, LockoutMinutes = 0, InviteTokenHours = 10_000 }.Validate().ToError();

        Assert.Equal(["inviteTokenHours", "lockoutMinutes", "passwordMinLength"], errors.FieldErrors!.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Audit_changes_skip_unchanged_fields()
    {
        var changes = new AuditChanges()
            .Change("name", "a", "a")
            .Change("role", "Developer", "Team Leader");

        Assert.False(changes.IsEmpty);
        Assert.Equal(["role"], changes.ToDictionary().Keys);
    }
}
