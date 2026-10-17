using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using TaskManagement.Api.Common;
using TaskManagement.Application.Access;

namespace TaskManagement.Api.Auth;

internal static class AuthSetup
{
    public const string SignInRateLimit = "auth-sign-in";
    public const string PasswordRateLimit = "auth-password";

    public static IServiceCollection AddApiAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.Section));
        var securePolicy = options.RequireSecureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;

        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(cookie =>
            {
                cookie.Cookie.Name = options.CookieName;
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Lax;
                cookie.Cookie.SecurePolicy = securePolicy;
                cookie.Cookie.Path = "/";
                cookie.ExpireTimeSpan = options.SessionLifetime;
                cookie.SlidingExpiration = true;
                cookie.Events = new CookieAuthenticationEvents
                {
                    OnValidatePrincipal = ValidatePrincipalAsync,

                    // An API answers 401/403 instead of redirecting to a login page.
                    OnRedirectToLogin = context => WriteProblemAsync(context.HttpContext, StatusCodes.Status401Unauthorized, "auth.required", "Please sign in to continue."),
                    OnRedirectToAccessDenied = context => WriteProblemAsync(context.HttpContext, StatusCodes.Status403Forbidden, "auth.forbidden", "You do not have permission to do this."),
                };
            });

        AddDataProtectionKeys(services, configuration);

        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddAuthorization();

        services.AddAntiforgery(antiforgery =>
        {
            antiforgery.HeaderName = "X-XSRF-TOKEN";
            antiforgery.Cookie.Name = options.AntiforgeryCookieName;
            antiforgery.Cookie.SameSite = SameSiteMode.Strict;
            antiforgery.Cookie.SecurePolicy = securePolicy;
        });

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (context, _) => new ValueTask(WriteProblemAsync(
                context.HttpContext, StatusCodes.Status429TooManyRequests, "rate_limited", "Too many requests. Please wait a moment and try again."));
            limiter.AddPolicy(SignInRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
                ClientIp(http), _ => new FixedWindowRateLimiterOptions { PermitLimit = options.SignInPermitsPerMinute, Window = TimeSpan.FromMinutes(1) }));
            limiter.AddPolicy(PasswordRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
                ClientIp(http), _ => new FixedWindowRateLimiterOptions { PermitLimit = options.PasswordPermitsPerQuarterHour, Window = TimeSpan.FromMinutes(15) }));
        });

        return services;
    }

    /// <summary>
    /// Data Protection keys encrypt session and antiforgery cookies (and, later, GitLab tokens). They must survive
    /// restarts and be shared by all instances, and live outside the database (docs/security.md S14):
    /// <c>DataProtection:KeysPath</c> is a protected volume; <c>DataProtection:CertificatePath</c> (+ password from a
    /// secret) encrypts the keys at rest. Without a path, keys stay in the local user profile (development only).
    /// </summary>
    private static void AddDataProtectionKeys(IServiceCollection services, IConfiguration configuration)
    {
        var dataProtection = services.AddDataProtection().SetApplicationName("TaskManagement");
        var keysPath = configuration["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        var certificatePath = configuration["DataProtection:CertificatePath"];
        if (!string.IsNullOrWhiteSpace(certificatePath))
        {
            dataProtection.ProtectKeysWithCertificate(
                X509CertificateLoader.LoadPkcs12FromFile(certificatePath, configuration["DataProtection:CertificatePassword"]));
        }
    }

    /// <summary>Runs on every authenticated request: rejects deactivated members and sessions with an old security stamp.</summary>
    private static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        var validator = context.HttpContext.RequestServices.GetRequiredService<SessionValidator>();
        if (principal is null
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !await validator.IsValidAsync(userId, principal.FindFirstValue(AuthClaims.SecurityStamp), context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    private static string ClientIp(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static async Task WriteProblemAsync(HttpContext http, int status, string code, string title)
    {
        http.Response.StatusCode = status;
        var problemDetails = http.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Extensions = { [ProblemDetailsSetup.CodeExtension] = code },
            },
        });
    }
}
