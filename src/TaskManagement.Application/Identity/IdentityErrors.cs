using TaskManagement.Domain.Common;

namespace TaskManagement.Application.Identity;

internal static class IdentityErrors
{
    /// <summary>
    /// One message for every sign-in failure (unknown email, wrong password, inactive, locked) so the response
    /// does not reveal whether an account exists (FR-AUTH-06).
    /// </summary>
    public static readonly Error InvalidCredentials = Error.Unauthorized(
        "auth.invalid_credentials",
        "Email or password is incorrect, or the account is temporarily locked. Try again later or reset your password.");

    public static readonly Error InvalidToken = Error.Validation(
        "auth.invalid_token",
        "This link is invalid, has already been used, or has expired. Request a new one.");
}
