using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using TaskManagement.Application.Access;
using TaskManagement.Domain.Access;

namespace TaskManagement.Api.Auth;

/// <summary>Requires a permission code with at least a given scope (FR-ROLE-05). Checked on the server for every request.</summary>
internal sealed record PermissionRequirement(string Code, PermissionScope MinimumScope) : IAuthorizationRequirement;

internal sealed class PermissionAuthorizationHandler(PermissionService permissions) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return;
        }

        var effective = await permissions.GetAsync(userId, CancellationToken.None);
        if (effective.Has(requirement.Code, requirement.MinimumScope))
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>Builds policies named <c>perm:{code}:{scope}</c> on demand, so endpoints need no policy registration.</summary>
internal sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    private const string Prefix = "perm:";

    public static string PolicyName(string code, PermissionScope scope) => $"{Prefix}{code}:{scope}";

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        ArgumentNullException.ThrowIfNull(policyName);
        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return await base.GetPolicyAsync(policyName);
        }

        var parts = policyName[Prefix.Length..].Split(':');
        if (parts.Length != 2 || !Enum.TryParse<PermissionScope>(parts[1], out var scope))
        {
            return null;
        }

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(parts[0], scope))
            .Build();
    }
}

internal static class PermissionEndpointExtensions
{
    /// <summary>Requires the permission. Endpoints acting on the caller's own data pass <see cref="PermissionScope.Own"/>.</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string code, PermissionScope minimumScope = PermissionScope.Global)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(PermissionPolicyProvider.PolicyName(code, minimumScope));
}
