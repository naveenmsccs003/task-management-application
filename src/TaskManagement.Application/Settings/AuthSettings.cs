using TaskManagement.Application.Common;

namespace TaskManagement.Application.Settings;

/// <summary>Authentication settings editable by Admins (FR-ROLE-08). Defaults follow FR-AUTH-04/05/07/09.</summary>
public sealed record AuthSettings
{
    public const string Key = "auth";

    public int PasswordMinLength { get; init; } = 12;

    public int MaxFailedSignInAttempts { get; init; } = 5;

    public int LockoutMinutes { get; init; } = 15;

    public int ResetTokenMinutes { get; init; } = 30;

    public int InviteTokenHours { get; init; } = 72;

    internal FieldErrors Validate()
    {
        var errors = new FieldErrors();
        Range(errors, nameof(PasswordMinLength), PasswordMinLength, 8, 128);
        Range(errors, nameof(MaxFailedSignInAttempts), MaxFailedSignInAttempts, 3, 20);
        Range(errors, nameof(LockoutMinutes), LockoutMinutes, 1, 1440);
        Range(errors, nameof(ResetTokenMinutes), ResetTokenMinutes, 5, 1440);
        Range(errors, nameof(InviteTokenHours), InviteTokenHours, 1, 720);
        return errors;
    }

    private static void Range(FieldErrors errors, string name, int value, int min, int max)
    {
        if (value < min || value > max)
        {
            errors.Add(char.ToLowerInvariant(name[0]) + name[1..], $"Must be between {min} and {max}.");
        }
    }
}
