using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace TaskManagement.Api.Auth;

internal static class AuthClaims
{
    public const string SecurityStamp = "tma:stamp";

    public static ClaimsPrincipal CreatePrincipal(Guid userId, string securityStamp) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(SecurityStamp, securityStamp),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme));
}
