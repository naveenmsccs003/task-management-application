using TaskManagement.Api.Auth;
using TaskManagement.Api.Common;
using TaskManagement.Application.Common;
using TaskManagement.Application.Notifications;
using TaskManagement.Domain.Access;

namespace TaskManagement.Api.Endpoints;

/// <summary>Own notifications and preferences (docs/api-design.md §4.10, §4.1) and the Admin system status (§4.12).</summary>
internal static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder api)
    {
        var notifications = api.MapGroup("/notifications").WithTags("Notifications")
            .RequirePermission(PermissionCatalog.NotificationsView, PermissionScope.Own);

        notifications.MapGet("/", async (bool? unreadOnly, int? page, int? pageSize, NotificationService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(unreadOnly ?? false, new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize), ct)));
        notifications.MapGet("/unread-count", async (NotificationService service, CancellationToken ct) =>
            TypedResults.Ok(new { count = await service.UnreadCountAsync(ct) }));
        notifications.MapPost("/{id:guid}/read", async (Guid id, NotificationService service, CancellationToken ct) =>
            (await service.MarkReadAsync(id, ct)).ToNoContentResult());
        notifications.MapPost("/read-all", async (NotificationService service, CancellationToken ct) =>
            TypedResults.Ok(new { marked = await service.MarkAllReadAsync(ct) }));

        var preferences = api.MapGroup("/me/notification-preferences").WithTags("Profile")
            .RequirePermission(PermissionCatalog.ProfileManage, PermissionScope.Own);
        preferences.MapGet("/", async (NotificationService service, CancellationToken ct) => TypedResults.Ok(await service.PreferencesAsync(ct)));
        preferences.MapPut("/", async (SaveNotificationPreferencesCommand command, NotificationService service, CancellationToken ct) =>
            (await service.SavePreferencesAsync(command, ct)).ToHttpResult());

        api.MapGet("/admin/system-status", async (JobRecorder jobs, CancellationToken ct) => TypedResults.Ok(await jobs.StatusAsync(ct)))
            .WithTags("Administration")
            .RequirePermission(PermissionCatalog.SystemHealth)
            .WithSummary("Background jobs and the email queue.");

        return api;
    }
}
