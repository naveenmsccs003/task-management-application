using TaskManagement.Api.Common;
using TaskManagement.Application.Common;
using TaskManagement.Application.Tasks;
using TaskManagement.Domain.Tasks;

namespace TaskManagement.Api.Endpoints;

/// <summary>
/// Tasks, workflow actions and comments (docs/api-design.md §4.4–4.5). Task permissions are project- or own-scoped,
/// so the handlers check them per task; a task the caller cannot see answers 404.
/// </summary>
internal static class TaskEndpoints
{
    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder api)
    {
        var tasks = api.MapGroup("/tasks").WithTags("Tasks").RequireAuthorization();

        tasks.MapGet("/", async (
                Guid? projectId, TaskItemStatus[]? status, Guid? assigneeId, TaskPriority[]? priority, DateOnly? dueFrom, DateOnly? dueTo,
                bool? overdue, string? q, string? sort, int? page, int? pageSize, TaskQueries queries, CancellationToken ct) =>
                (await queries.ListAsync(
                    new TaskListQuery(projectId, status, assigneeId, priority, dueFrom, dueTo, overdue, q, sort,
                        new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize)), ct)).ToHttpResult())
            .WithSummary("tasks.view — tasks the caller can see, filtered, sorted and paginated.");

        tasks.MapPost("/", async Task<IResult> (CreateTaskCommand command, TaskCommands commands, CancellationToken ct) =>
                Created(await commands.CreateAsync(command, ct)))
            .WithSummary("tasks.create — projectId is required in the body.");

        api.MapPost("/projects/{projectId:guid}/tasks", async Task<IResult> (
                Guid projectId, CreateTaskCommand command, TaskCommands commands, CancellationToken ct) =>
                Created(await commands.CreateAsync(command with { ProjectId = projectId }, ct)))
            .WithTags("Tasks")
            .RequireAuthorization()
            .WithSummary("tasks.create");

        tasks.MapGet("/by-key/{key}", async (string key, TaskQueries queries, CancellationToken ct) =>
                (await queries.GetByKeyAsync(key, ct)).ToHttpResult())
            .WithSummary("tasks.view — look up a task by its key, e.g. TMA-42.");

        tasks.MapGet("/{id:guid}", async (Guid id, TaskQueries queries, CancellationToken ct) =>
                (await queries.GetAsync(id, ct)).ToHttpResult())
            .WithSummary("tasks.view — includes the actions the caller may take now.");

        tasks.MapPatch("/{id:guid}", async (Guid id, UpdateTaskCommand command, TaskCommands commands, CancellationToken ct) =>
                (await commands.UpdateAsync(id, command, ct)).ToHttpResult())
            .WithSummary("tasks.update");

        tasks.MapDelete("/{id:guid}", async (Guid id, TaskCommands commands, CancellationToken ct) =>
                (await commands.DeleteAsync(id, ct)).ToNoContentResult())
            .WithSummary("tasks.delete-draft — only drafts without history.");

        tasks.MapGet("/{id:guid}/history", async (Guid id, TaskQueries queries, CancellationToken ct) =>
                (await queries.HistoryAsync(id, ct)).ToHttpResult())
            .WithSummary("tasks.view — status and assignment timeline.");

        tasks.MapPost("/{id:guid}/assign", async (Guid id, AssignTaskCommand command, TaskCommands commands, CancellationToken ct) =>
                (await commands.AssignAsync(id, command, ct)).ToHttpResult())
            .WithSummary("tasks.assign — T2, first assignment of a draft.");

        tasks.MapPost("/{id:guid}/cancel", async (Guid id, CancelTaskCommand command, TaskCommands commands, CancellationToken ct) =>
                (await commands.CancelAsync(id, command, ct)).ToHttpResult())
            .WithSummary("tasks.cancel — T15, reason required.");

        tasks.MapGet("/{id:guid}/comments", async (Guid id, TaskComments comments, CancellationToken ct) =>
                (await comments.ListAsync(id, ct)).ToHttpResult())
            .WithSummary("tasks.view");

        tasks.MapPost("/{id:guid}/comments", async Task<IResult> (Guid id, SaveCommentCommand command, TaskComments comments, CancellationToken ct) =>
            {
                var result = await comments.AddAsync(id, command, ct);
                return result.IsSuccess
                    ? TypedResults.Created($"/api/v1/tasks/{id}/comments/{result.Value.Id}", result.Value)
                    : result.Error!.ToProblem();
            })
            .WithSummary("tasks.comment");

        tasks.MapPatch("/{id:guid}/comments/{commentId:guid}", async (
                Guid id, Guid commentId, SaveCommentCommand command, TaskComments comments, CancellationToken ct) =>
                (await comments.EditAsync(id, commentId, command, ct)).ToHttpResult())
            .WithSummary("Author only, within 15 minutes of posting.");

        tasks.MapDelete("/{id:guid}/comments/{commentId:guid}", async (Guid id, Guid commentId, TaskComments comments, CancellationToken ct) =>
                (await comments.DeleteAsync(id, commentId, ct)).ToNoContentResult())
            .WithSummary("Author, or a member with tasks.update in the project.");

        return api;
    }

    private static IResult Created(Domain.Common.Result<TaskDto> result) =>
        result.IsSuccess
            ? TypedResults.Created($"/api/v1/tasks/{result.Value.Id}", result.Value)
            : result.Error!.ToProblem();
}
