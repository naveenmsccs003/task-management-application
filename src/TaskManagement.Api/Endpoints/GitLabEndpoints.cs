using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using TaskManagement.Api.Auth;
using TaskManagement.Api.Common;
using TaskManagement.Application.GitLab;
using TaskManagement.Domain.Access;

namespace TaskManagement.Api.Endpoints;

/// <summary>
/// GitLab connections, project links, issue import and sync (docs/api-design.md §4.11). Connections are Admin-only;
/// project actions need gitlab.import in that project, checked by the handlers. The webhook is anonymous and is mapped
/// outside the XSRF-protected group: it is authenticated by its secret instead.
/// </summary>
internal static class GitLabEndpoints
{
    public const string WebhookRateLimit = "gitlab-webhook";

    public static IServiceCollection AddGitLabEndpoints(this IServiceCollection services) =>
        services.Configure<RateLimiterOptions>(limiter => limiter.AddPolicy(WebhookRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) })));

    public static IEndpointRouteBuilder MapGitLabEndpoints(this IEndpointRouteBuilder api)
    {
        var connections = api.MapGroup("/gitlab/connections").WithTags("GitLab").RequireAuthorization();
        connections.MapGet("/", async (GitLabConnections service, CancellationToken ct) => TypedResults.Ok(await service.ListAsync(ct)))
            .RequirePermission(PermissionCatalog.GitLabConnectionsManage)
            .WithSummary("gitlab.connections.manage — tokens are never returned, only their last characters.");
        connections.MapPost("/", async (SaveGitLabConnectionCommand command, GitLabConnections service, CancellationToken ct) =>
                (await service.CreateAsync(command, ct)).ToHttpResult())
            .RequirePermission(PermissionCatalog.GitLabConnectionsManage)
            .WithSummary("gitlab.connections.manage — { name, baseUrl, token }.");
        connections.MapPatch("/{id:guid}", async (Guid id, SaveGitLabConnectionCommand command, GitLabConnections service, CancellationToken ct) =>
                (await service.UpdateAsync(id, command, ct)).ToHttpResult())
            .RequirePermission(PermissionCatalog.GitLabConnectionsManage)
            .WithSummary("gitlab.connections.manage — token replaced only when given.");
        connections.MapDelete("/{id:guid}", async (Guid id, GitLabConnections service, CancellationToken ct) =>
                (await service.DeleteAsync(id, ct)).ToNoContentResult())
            .RequirePermission(PermissionCatalog.GitLabConnectionsManage)
            .WithSummary("gitlab.connections.manage — refused while projects or tasks are linked.");
        connections.MapPost("/{id:guid}/test", async (Guid id, GitLabConnections service, CancellationToken ct) =>
                (await service.TestAsync(id, ct)).ToHttpResult())
            .RequirePermission(PermissionCatalog.GitLabConnectionsManage)
            .WithSummary("gitlab.connections.manage — calls GitLab and records the result.");
        connections.MapGet("/{id:guid}/projects", async (Guid id, string? search, GitLabImports service, CancellationToken ct) =>
                (await service.SearchProjectsAsync(id, search, ct)).ToHttpResult())
            .WithSummary("gitlab.import (any project) — GitLab projects the token can see.");

        api.MapGet("/gitlab/connection-options", async (GitLabConnections service, CancellationToken ct) => (await service.OptionsAsync(ct)).ToHttpResult())
            .WithTags("GitLab")
            .RequireAuthorization()
            .WithSummary("gitlab.import (any project) — connection names for the link form.");

        var projects = api.MapGroup("/projects/{id:guid}").WithTags("GitLab").RequireAuthorization();
        projects.MapGet("/gitlab-link", async (Guid id, GitLabImports service, CancellationToken ct) =>
            {
                var result = await service.GetLinkAsync(id, ct);
                return result.IsSuccess && result.Value is null ? TypedResults.NoContent() : result.ToHttpResult();
            })
            .WithSummary("gitlab.import — 204 when the project is not linked.");
        projects.MapPut("/gitlab-link", async (Guid id, LinkGitLabProjectCommand command, GitLabImports service, CancellationToken ct) =>
                (await service.LinkAsync(id, command, ct)).ToHttpResult())
            .WithSummary("gitlab.import — { connectionId, gitLabProjectId, syncEnabled }.");
        projects.MapDelete("/gitlab-link", async (Guid id, GitLabImports service, CancellationToken ct) => (await service.UnlinkAsync(id, ct)).ToNoContentResult())
            .WithSummary("gitlab.import — imported tasks keep their GitLab links but are no longer synced.");
        projects.MapPost("/gitlab-link/webhook-secret", async (Guid id, GitLabImports service, CancellationToken ct) =>
                (await service.NewWebhookSecretAsync(id, ct)).ToHttpResult())
            .WithSummary("gitlab.import — a new webhook secret, shown once.");
        projects.MapGet("/gitlab/issues", async (Guid id, string? state, string? search, int? page, GitLabImports service, CancellationToken ct) =>
                (await service.PreviewIssuesAsync(id, state, search, page, ct)).ToHttpResult())
            .WithSummary("gitlab.import — issues with an \"already imported\" marker.");
        projects.MapPost("/gitlab/import", async (Guid id, ImportIssuesCommand command, GitLabImports service, CancellationToken ct) =>
                (await service.ImportAsync(id, command, ct)).ToHttpResult())
            .WithSummary("gitlab.import — { issueIids[] }; imported issues become Draft tasks, never twice.");
        projects.MapPost("/gitlab/sync", async (Guid id, GitLabImports service, CancellationToken ct) => (await service.SyncNowAsync(id, ct)).ToHttpResult())
            .WithSummary("gitlab.import — sync now.");
        projects.MapGet("/gitlab/import-runs", async (Guid id, GitLabImports service, CancellationToken ct) => (await service.RunsAsync(id, ct)).ToHttpResult())
            .WithSummary("gitlab.import — the last 20 imports and syncs.");

        api.MapPost("/tasks/{id:guid}/gitlab/accept-changes", async (Guid id, GitLabImports service, CancellationToken ct) =>
                (await service.AcceptChangesAsync(id, ct)).ToHttpResult())
            .WithTags("GitLab")
            .RequireAuthorization()
            .WithSummary("tasks.update — apply the title and description changed in GitLab.");
        return api;
    }

    /// <summary>
    /// POST /api/v1/webhooks/gitlab/{linkId} (FR-IMP-06): anonymous, no cookie or XSRF; the <c>X-Gitlab-Token</c> header must
    /// match the link's secret. Only issue events are used, and only as a trigger to re-read that issue.
    /// </summary>
    public static IEndpointRouteBuilder MapGitLabWebhook(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/webhooks/gitlab/{linkId:guid}", async (Guid linkId, HttpRequest request, GitLabImports service, CancellationToken ct) =>
            {
                long? iid = null;
                if (request.ContentLength is null or <= 1_000_000)
                {
                    try
                    {
                        using var payload = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
                        var root = payload.RootElement;
                        if (root.TryGetProperty("object_kind", out var kind) && kind.GetString() == "issue"
                            && root.TryGetProperty("object_attributes", out var attributes) && attributes.TryGetProperty("iid", out var value)
                            && value.TryGetInt64(out var parsed))
                        {
                            iid = parsed;
                        }
                    }
                    catch (JsonException)
                    {
                        // Not JSON: treated like any other event we do not use.
                    }
                }

                return (await service.HandleWebhookAsync(linkId, request.Headers["X-Gitlab-Token"].FirstOrDefault(), iid, ct)).ToNoContentResult();
            })
            .WithTags("GitLab")
            .AllowAnonymous()
            .RequireRateLimiting(WebhookRateLimit)
            .WithSummary("Anonymous; X-Gitlab-Token must match the link's secret. Re-reads the issue from GitLab.");
        return app;
    }
}
