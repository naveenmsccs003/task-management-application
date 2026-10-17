namespace TaskManagement.Api.Common;

internal static class EnvironmentNames
{
    /// <summary>The shared QA environment, also used by integration tests (docs/deployment.md §1).</summary>
    public const string Testing = "Testing";

    public static bool IsTesting(this IHostEnvironment environment) => environment.IsEnvironment(Testing);
}
