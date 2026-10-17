namespace TaskManagement.Domain.Identity;

public enum PasswordTokenPurpose
{
    /// <summary>"Forgot password" or admin-initiated reset (FR-AUTH-07).</summary>
    Reset,

    /// <summary>"Set your password" link for a new account (FR-AUTH-09).</summary>
    Invite,
}
