using TaskManagement.Api.Auth;
using TaskManagement.Api.Common;
using TaskManagement.Application.Common;
using TaskManagement.Application.Members;
using TaskManagement.Domain.Access;

namespace TaskManagement.Api.Endpoints;

internal static class MemberEndpoints
{
    public static IEndpointRouteBuilder MapMemberEndpoints(this IEndpointRouteBuilder api)
    {
        var members = api.MapGroup("/members").WithTags("Members");

        members.MapGet("/", async (string? search, bool? isActive, Guid? roleId, int? page, int? pageSize, MemberQueries queries, CancellationToken ct) =>
                TypedResults.Ok(await queries.ListAsync(
                    new MemberListQuery(search, isActive, roleId, new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize)), ct)))
            .RequirePermission(PermissionCatalog.MembersView);

        members.MapGet("/{id:guid}", async (Guid id, MemberQueries queries, CancellationToken ct) =>
                (await queries.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission(PermissionCatalog.MembersView);

        members.MapPost("/", async Task<IResult> (CreateMemberCommand command, MemberCommands commands, CancellationToken ct) =>
            {
                var result = await commands.CreateAsync(command, ct);
                return result.IsSuccess
                    ? TypedResults.Created($"/api/v1/members/{result.Value.Id}", result.Value)
                    : result.Error!.ToProblem();
            })
            .RequirePermission(PermissionCatalog.MembersCreate)
            .WithSummary("Creates a member and emails them a link to set their password.");

        members.MapPatch("/{id:guid}", async (Guid id, UpdateMemberCommand command, MemberCommands commands, CancellationToken ct) =>
                (await commands.UpdateAsync(id, command, ct)).ToHttpResult())
            .RequirePermission(PermissionCatalog.MembersUpdate);

        members.MapPost("/{id:guid}/deactivate", async (Guid id, MemberCommands commands, CancellationToken ct) =>
                (await commands.DeactivateAsync(id, ct)).ToHttpResult())
            .RequirePermission(PermissionCatalog.MembersUpdate);

        members.MapPost("/{id:guid}/reactivate", async (Guid id, MemberCommands commands, CancellationToken ct) =>
                (await commands.ReactivateAsync(id, ct)).ToHttpResult())
            .RequirePermission(PermissionCatalog.MembersUpdate);

        members.MapPost("/{id:guid}/send-password-reset", async (Guid id, MemberCommands commands, CancellationToken ct) =>
                (await commands.SendPasswordResetAsync(id, ct)).ToNoContentResult())
            .RequirePermission(PermissionCatalog.MembersResetPassword);

        return api;
    }
}
