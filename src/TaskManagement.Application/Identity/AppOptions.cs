namespace TaskManagement.Application.Identity;

/// <summary>Bound from the <c>App</c> configuration section.</summary>
public sealed class AppOptions
{
    public const string Section = "App";

    /// <summary>Public URL of the Angular app, used in email links (e.g. <c>https://tasks.example.com</c>).</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:4200";

    public string ProductName { get; set; } = "Task Management";
}
