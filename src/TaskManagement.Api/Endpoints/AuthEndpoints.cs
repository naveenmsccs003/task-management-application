using System.Diagnostics;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using TaskManagement.Api.Auth;
using TaskManagement.Api.Common;
using TaskManagement.Application.Identity;
using TaskManagement.Domain.Access;

namespace TaskManagement.Api.Endpoints;

internal static class AuthEndpoints
{
    /// <summary>Forgot-password responses take at least this long, so timing does not reveal whether an account exists.</summary>
    private static readonly TimeSpan ForgotPasswordMinimumDuration = TimeSpan.FromMilliseconds(400);

    public sealed record AntiforgeryResponse(string RequestToken);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Authentication");

        // Angular calls this on start-up and after sign-in/sign-out (tokens are tied to the signed-in user).
        auth.MapGet("/antiforgery", (HttpContext http, IAntiforgery antiforgery) =>
            {
                var tokens = antiforgery.GetAndStoreTokens(http);
                http.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
                {
                    HttpOnly = false, // Angular must read it and echo it in the X-XSRF-TOKEN header.
                    SameSite = SameSiteMode.Strict,
                    Secure = http.Request.IsHttps,
                    Path = "/",
                });
                return TypedResults.Ok(new AntiforgeryResponse(tokens.RequestToken!));
            })
            .AllowAnonymous();

        auth.MapPost("/login", async Task<IResult> (SignInCommand command, SignInHandler handler, MeHandler me, HttpContext http, CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(command, ct);
                if (result.IsFailure)
                {
                    return result.Error!.ToProblem();
                }

                var principal = AuthClaims.CreatePrincipal(result.Value.UserId, result.Value.SecurityStamp);
                await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
                http.User = principal;
                return (await me.GetAsync(ct)).ToHttpResult();
            })
            .AllowAnonymous()
            .RequireRateLimiting(AuthSetup.SignInRateLimit)
            .WithSummary("Signs in with email and password and sets the session cookie.");

        auth.MapPost("/logout", async (HttpContext http) =>
            {
                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return TypedResults.NoContent();
            })
            .AllowAnonymous();

        auth.MapPost("/forgot-password", async (RequestPasswordResetCommand command, RequestPasswordResetHandler handler, CancellationToken ct) =>
            {
                var stopwatch = Stopwatch.StartNew();
                await handler.HandleAsync(command, ct);
                var remaining = ForgotPasswordMinimumDuration - stopwatch.Elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, ct);
                }

                return TypedResults.Accepted((string?)null);
            })
            .AllowAnonymous()
            .RequireRateLimiting(AuthSetup.PasswordRateLimit)
            .WithSummary("Emails a reset link if the account exists. Always returns 202.");

        auth.MapPost("/reset-password", async (ResetPasswordCommand command, ResetPasswordHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(command, ct)).ToNoContentResult())
            .AllowAnonymous()
            .RequireRateLimiting(AuthSetup.PasswordRateLimit)
            .WithSummary("Sets a new password with a reset or invite token.");

        auth.MapPost("/change-password", async Task<IResult> (ChangePasswordCommand command, ChangePasswordHandler handler, HttpContext http, CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(command, ct);
                if (result.IsFailure)
                {
                    return result.Error!.ToProblem();
                }

                // Keep this session; every other session is now invalid because the security stamp changed.
                await http.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    AuthClaims.CreatePrincipal(result.Value.UserId, result.Value.SecurityStamp));
                return TypedResults.NoContent();
            })
            .RequirePermission(PermissionCatalog.ProfileManage, PermissionScope.Own);

        return api;
    }
}
