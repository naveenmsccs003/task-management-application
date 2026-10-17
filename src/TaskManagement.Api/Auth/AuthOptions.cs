namespace TaskManagement.Api.Auth;

/// <summary>Bound from the <c>Auth</c> configuration section.</summary>
internal sealed class AuthOptions
{
    public const string Section = "Auth";

    /// <summary>
    /// <c>__Host-</c> cookies must be Secure, host-only and path=/ — the strictest browser cookie rules.
    /// Development over plain http uses names without the prefix.
    /// </summary>
    public string CookieName { get; set; } = "__Host-tma";

    public string AntiforgeryCookieName { get; set; } = "__Host-tma-af";

    public bool RequireSecureCookies { get; set; } = true;

    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Sign-in attempts allowed per client IP per minute (in addition to account lockout).</summary>
    public int SignInPermitsPerMinute { get; set; } = 10;

    /// <summary>Forgot/reset-password requests allowed per client IP per 15 minutes.</summary>
    public int PasswordPermitsPerQuarterHour { get; set; } = 5;
}
