using System.Reflection;

namespace TaskManagement.Api.Endpoints;

internal static class SystemEndpoints
{
    public sealed record SystemInfoResponse(string Name, string Version, DateTimeOffset ServerTimeUtc);

    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/system").WithTags("System");

        // Public on purpose: lets the UI show the version and detect client clock skew. Exposes no configuration.
        group.MapGet("/info", (TimeProvider timeProvider) =>
                TypedResults.Ok(new SystemInfoResponse("Task Management", AppVersion, timeProvider.GetUtcNow())))
            .AllowAnonymous()
            .WithName("GetSystemInfo")
            .WithSummary("Application name, version and current server time (UTC).");

        return api;
    }

    private static readonly string AppVersion =
        typeof(SystemEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0]
        ?? "0.0.0";
}
