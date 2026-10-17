using TaskManagement.Api.Auth;
using TaskManagement.Api.Common;
using TaskManagement.Application.Common;
using TaskManagement.Application.Projects;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Projects;

namespace TaskManagement.Api.Endpoints;

/// <summary>
/// Projects and their members (docs/api-design.md §4.3). Permissions here depend on the caller's role in the project,
/// so the endpoints require a signed-in member and the handlers check projects.view / projects.update /
/// projects.members.manage per project. Creating a project needs a Global projects.create grant.
/// </summary>
internal static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder api)
    {
        var projects = api.MapGroup("/projects").WithTags("Projects").RequireAuthorization();

        projects.MapGet("/", async (string? search, ProjectStatus? status, int? page, int? pageSize, ProjectQueries queries, CancellationToken ct) =>
                TypedResults.Ok(await queries.ListAsync(
                    new ProjectListQuery(search, status, new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize)), ct)))
            .WithSummary("Projects the caller can see (projects.view).");

        projects.MapPost("/", async Task<IResult> (SaveProjectCommand command, ProjectCommands commands, CancellationToken ct) =>
            {
                var result = await commands.CreateAsync(command, ct);
                return result.IsSuccess
                    ? TypedResults.Created($"/api/v1/projects/{result.Value.Id}", result.Value)
                    : result.Error!.ToProblem();
            })
            .RequirePermission(PermissionCatalog.ProjectsCreate)
            .WithSummary("Creates a project. A creator who cannot see every project becomes its Project Manager.");

        projects.MapGet("/roles", async (ProjectQueries queries, CancellationToken ct) => TypedResults.Ok(await queries.ProjectRolesAsync(ct)))
            .WithSummary("Roles that can be given to project members (all roles except Admin).");

        projects.MapGet("/{id:guid}", async (Guid id, ProjectQueries queries, CancellationToken ct) =>
                (await queries.GetAsync(id, ct)).ToHttpResult())
            .WithSummary("projects.view");

        projects.MapPatch("/{id:guid}", async (Guid id, SaveProjectCommand command, ProjectCommands commands, CancellationToken ct) =>
                (await commands.UpdateAsync(id, command, ct)).ToHttpResult())
            .WithSummary("projects.update");

        foreach (var (path, target) in new[]
        {
            ("hold", ProjectStatus.OnHold),
            ("complete", ProjectStatus.Completed),
            ("archive", ProjectStatus.Archived),
            ("reactivate", ProjectStatus.Active),
        })
        {
            projects.MapPost($"/{{id:guid}}/{path}", async (Guid id, ProjectStatusCommand? command, ProjectCommands commands, CancellationToken ct) =>
                    (await commands.ChangeStatusAsync(id, target, command ?? new ProjectStatusCommand(false), ct)).ToHttpResult())
                .WithSummary($"projects.update — moves the project to {target}.");
        }

        projects.MapGet("/{id:guid}/my-permissions", async (Guid id, ProjectQueries queries, CancellationToken ct) =>
                (await queries.MyPermissionsAsync(id, ct)).ToHttpResult())
            .WithSummary("The caller's effective permissions in the project (for the UI only).");

        projects.MapGet("/{id:guid}/members", async (Guid id, ProjectQueries queries, CancellationToken ct) =>
                (await queries.MembersAsync(id, ct)).ToHttpResult())
            .WithSummary("projects.view");

        projects.MapGet("/{id:guid}/member-candidates", async (Guid id, string? search, ProjectMemberCommands commands, CancellationToken ct) =>
                (await commands.CandidatesAsync(id, search, ct)).ToHttpResult())
            .WithSummary("projects.members.manage — active accounts that are not members yet.");

        projects.MapPost("/{id:guid}/members", async (Guid id, AddProjectMemberCommand command, ProjectMemberCommands commands, CancellationToken ct) =>
                (await commands.AddAsync(id, command, ct)).ToHttpResult())
            .WithSummary("projects.members.manage");

        projects.MapPatch("/{id:guid}/members/{userId:guid}", async (
                Guid id, Guid userId, ChangeProjectMemberCommand command, ProjectMemberCommands commands, CancellationToken ct) =>
                (await commands.ChangeRoleAsync(id, userId, command, ct)).ToHttpResult())
            .WithSummary("projects.members.manage");

        projects.MapDelete("/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId, ProjectMemberCommands commands, CancellationToken ct) =>
                (await commands.RemoveAsync(id, userId, ct)).ToNoContentResult())
            .WithSummary("projects.members.manage — blocked while the member has open tasks in the project.");

        return api;
    }
}
