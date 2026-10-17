using TaskManagement.Api.Auth;
using TaskManagement.Api.Common;
using TaskManagement.Application.Access;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Application.Settings;
using TaskManagement.Domain.Access;

namespace TaskManagement.Api.Endpoints;

internal static class AdministrationEndpoints
{
    public static IEndpointRouteBuilder MapAdministrationEndpoints(this IEndpointRouteBuilder api)
    {
        var roles = api.MapGroup("/roles").WithTags("Roles");
        roles.MapGet("/", async (RoleManagement roleManagement, CancellationToken ct) => TypedResults.Ok(await roleManagement.ListAsync(ct)))
            .RequirePermission(PermissionCatalog.RolesView);
        roles.MapPost("/", async Task<IResult> (SaveRoleCommand command, RoleManagement roleManagement, CancellationToken ct) =>
            {
                var result = await roleManagement.CreateAsync(command, ct);
                return result.IsSuccess ? TypedResults.Created($"/api/v1/roles/{result.Value.Id}", result.Value) : result.Error!.ToProblem();
            })
            .RequirePermission(PermissionCatalog.RolesManage);
        roles.MapPatch("/{id:guid}", async (Guid id, SaveRoleCommand command, RoleManagement roleManagement, CancellationToken ct) =>
                (await roleManagement.UpdateAsync(id, command, ct)).ToHttpResult())
            .RequirePermission(PermissionCatalog.RolesManage);
        roles.MapDelete("/{id:guid}", async (Guid id, RoleManagement roleManagement, CancellationToken ct) =>
                (await roleManagement.DeleteAsync(id, ct)).ToNoContentResult())
            .RequirePermission(PermissionCatalog.RolesManage);
        roles.MapPut("/{id:guid}/permissions", async (Guid id, SetRolePermissionsCommand command, RoleManagement roleManagement, CancellationToken ct) =>
                (await roleManagement.SetPermissionsAsync(id, command, ct)).ToHttpResult())
            .RequirePermission(PermissionCatalog.RolesManage);

        api.MapGet("/permissions", async (RoleManagement roleManagement, CancellationToken ct) => TypedResults.Ok(await roleManagement.ListPermissionsAsync(ct)))
            .WithTags("Roles")
            .RequirePermission(PermissionCatalog.RolesView);

        var settings = api.MapGroup("/settings").WithTags("Settings");
        settings.MapGet("/", async (SettingsService service, CancellationToken ct) => TypedResults.Ok(await service.GetAllAsync(ct)))
            .RequirePermission(PermissionCatalog.SettingsManage);
        settings.MapPut("/auth", async (AuthSettings value, SettingsService service, CancellationToken ct) =>
                (await service.UpdateAuthAsync(value, ct)).ToHttpResult())
            .RequirePermission(PermissionCatalog.SettingsManage);

        api.MapGet("/audit-logs", async (
                DateTimeOffset? from, DateTimeOffset? to, Guid? actorId, string? action, string? entityType, string? entityId,
                int? page, int? pageSize, SearchAuditLogsHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(
                    new AuditLogQuery(from, to, actorId, action, entityType, entityId, new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize)), ct)))
            .WithTags("Audit")
            .RequirePermission(PermissionCatalog.AuditView);

        return api;
    }
}
