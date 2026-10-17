using TaskManagement.Api.Auth;
using TaskManagement.Api.Common;
using TaskManagement.Application.Identity;
using TaskManagement.Domain.Access;

namespace TaskManagement.Api.Endpoints;

internal static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder api)
    {
        var me = api.MapGroup("/me").WithTags("Profile");

        me.MapGet("/", async (MeHandler handler, CancellationToken ct) => (await handler.GetAsync(ct)).ToHttpResult())
            .RequireAuthorization()
            .WithSummary("The signed-in member's profile and effective permissions.");

        me.MapPatch("/", async (UpdateMyProfileCommand command, MeHandler handler, CancellationToken ct) =>
                (await handler.UpdateProfileAsync(command, ct)).ToHttpResult())
            .RequirePermission(PermissionCatalog.ProfileManage, PermissionScope.Own);

        return api;
    }
}
