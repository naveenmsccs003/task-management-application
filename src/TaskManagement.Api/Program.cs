using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using TaskManagement.Api.Auth;
using TaskManagement.Api.Cli;
using TaskManagement.Api.Common;
using TaskManagement.Api.Endpoints;
using TaskManagement.Api.Notifications;
using TaskManagement.Application;
using TaskManagement.Application.Common;
using TaskManagement.Application.Identity;
using TaskManagement.Application.Notifications;
using TaskManagement.Infrastructure;
using TaskManagement.Infrastructure.Persistence;
using TaskManagement.Infrastructure.Seeding;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logs everywhere except local development (NFR-OBS-01).
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(options =>
    {
        options.IncludeScopes = true;
        options.UseUtcTimestamp = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    });
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.Section));
builder.Services.AddApiProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi("v1");
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

// The app runs behind a reverse proxy that terminates TLS (docs/deployment.md §3).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxy in builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
    {
        options.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
    }
});

builder.Services.AddApiAuth(builder.Configuration);
// Same JSON shape as the REST API: camelCase properties, enums as strings.
builder.Services.AddSignalR().AddJsonProtocol(options =>
    options.PayloadSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddSingleton<INotificationPublisher, SignalRNotificationPublisher>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAttachmentUploads(builder.Configuration);

// Q9: uploads must be malware-scanned outside development.
if (builder.Environment.IsProduction()
    && builder.Configuration.GetValue("Attachments:Scanner", TaskManagement.Infrastructure.Files.MalwareScannerKind.ClamAv)
        == TaskManagement.Infrastructure.Files.MalwareScannerKind.None)
{
    throw new InvalidOperationException("Attachments:Scanner cannot be None in Production. Configure ClamAV (see docs/deployment.md).");
}

var app = builder.Build();

// Development convenience only. Testing and Production apply migrations with a migration bundle (docs/database-design.md §6).
if (app.Environment.IsDevelopment() && app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

// Permissions and system roles must exist before the first request (and before CLI commands).
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync(CancellationToken.None);
}

if (args.Length > 0 && args[0] == CreateAdminCommand.Name)
{
    return await CreateAdminCommand.RunAsync(app.Services, args);
}

// Fail at start-up, not at the first upload, when the private file storage is not configured.
app.Services.GetRequiredService<TaskManagement.Application.Attachments.IFileStorage>();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<SecurityHeadersMiddleware>();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment() || app.Environment.IsTesting())
{
    app.MapOpenApi("/openapi/{documentName}.json");
}

// Liveness: the process is running. Readiness: dependencies (database) are reachable.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

var api = app.MapGroup("/api/v1").AddEndpointFilter<AntiforgeryFilter>();
api.MapSystemEndpoints();
api.MapAuthEndpoints();
api.MapMeEndpoints();
api.MapMemberEndpoints();
api.MapAdministrationEndpoints();
api.MapProjectEndpoints();
api.MapTaskEndpoints();
api.MapWorkEndpoints();
api.MapNotificationEndpoints();
api.MapReviewEndpoints();
api.MapAttachmentEndpoints();
app.MapHub<NotificationHub>(NotificationHub.Path);

await app.RunAsync();
return 0;

/// <summary>Entry point; public so integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
