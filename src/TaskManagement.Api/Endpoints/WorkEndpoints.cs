using TaskManagement.Api.Common;
using TaskManagement.Application.Algorithms;
using TaskManagement.Application.Checklists;
using TaskManagement.Application.Common;
using TaskManagement.Application.Time;
using TaskManagement.Application.Work;

namespace TaskManagement.Api.Endpoints;

/// <summary>
/// Algorithm approval, work sessions, checklists and time (docs/api-design.md §4.5–4.7). Like the other task endpoints,
/// permissions depend on the caller's role in the task's project and on being the assignee, so the handlers check them.
/// </summary>
internal static class WorkEndpoints
{
    public static IEndpointRouteBuilder MapWorkEndpoints(this IEndpointRouteBuilder api)
    {
        var tasks = api.MapGroup("/tasks/{id:guid}").WithTags("Work").RequireAuthorization();

        // Work sessions (T6–T9). The client never sends times (FR-TIME-02).
        tasks.MapPost("/start", async (Guid id, WorkService work, CancellationToken ct) => (await work.StartAsync(id, ct)).ToHttpResult())
            .AddEndpointFilter<IdempotencyFilter>()
            .WithSummary("tasks.work (assignee) — T6. Accepts an Idempotency-Key header.");
        tasks.MapPost("/pause", async (Guid id, PauseCommand? command, WorkService work, CancellationToken ct) =>
                (await work.PauseAsync(id, command ?? new PauseCommand(null), ct)).ToHttpResult())
            .AddEndpointFilter<IdempotencyFilter>()
            .WithSummary("tasks.work (assignee) — T7.");
        tasks.MapPost("/resume", async (Guid id, WorkService work, CancellationToken ct) => (await work.ResumeAsync(id, ct)).ToHttpResult())
            .AddEndpointFilter<IdempotencyFilter>()
            .WithSummary("tasks.work (assignee) — T8.");
        tasks.MapPost("/stop", async (Guid id, StopCommand command, WorkService work, CancellationToken ct) =>
                (await work.StopAsync(id, command, ct)).ToHttpResult())
            .AddEndpointFilter<IdempotencyFilter>()
            .WithSummary("tasks.work (assignee) — T9, reason required.");

        // Algorithm (T3–T5).
        tasks.MapGet("/algorithm", async (Guid id, AlgorithmService algorithms, CancellationToken ct) => (await algorithms.ListAsync(id, ct)).ToHttpResult())
            .WithSummary("algorithms.view — every version, newest first.");
        tasks.MapPost("/algorithm", async (Guid id, SubmitAlgorithmCommand command, AlgorithmService algorithms, CancellationToken ct) =>
                (await algorithms.SubmitAsync(id, command, ct)).ToHttpResult())
            .WithSummary("algorithms.submit (assignee) — T3.");
        tasks.MapPost("/algorithm/{submissionId:guid}/approve", async (
                Guid id, Guid submissionId, AlgorithmDecisionCommand? command, AlgorithmService algorithms, CancellationToken ct) =>
                (await algorithms.ApproveAsync(id, submissionId, command ?? new AlgorithmDecisionCommand(null), ct)).ToHttpResult())
            .WithSummary("algorithms.approve — T4; not by the assignee.");
        tasks.MapPost("/algorithm/{submissionId:guid}/reject", async (
                Guid id, Guid submissionId, AlgorithmDecisionCommand command, AlgorithmService algorithms, CancellationToken ct) =>
                (await algorithms.RejectAsync(id, submissionId, command, ct)).ToHttpResult())
            .WithSummary("algorithms.approve — T5, comment required.");

        // Checklist.
        tasks.MapGet("/checklist", async (Guid id, TaskChecklist checklist, CancellationToken ct) => (await checklist.ListAsync(id, ct)).ToHttpResult())
            .WithSummary("tasks.view");
        tasks.MapPost("/checklist", async (Guid id, AddChecklistItemCommand command, TaskChecklist checklist, CancellationToken ct) =>
                (await checklist.AddItemAsync(id, command, ct)).ToHttpResult())
            .WithSummary("checklists.manage-project — adds a task-specific item before review.");
        tasks.MapPost("/checklist/{itemId:guid}/complete", async (
                Guid id, Guid itemId, CompleteChecklistItemCommand? command, TaskChecklist checklist, CancellationToken ct) =>
                (await checklist.CompleteAsync(id, itemId, command ?? new CompleteChecklistItemCommand(null), ct)).ToHttpResult())
            .WithSummary("checklists.complete (assignee)");
        tasks.MapPost("/checklist/{itemId:guid}/uncomplete", async (Guid id, Guid itemId, TaskChecklist checklist, CancellationToken ct) =>
                (await checklist.UncompleteAsync(id, itemId, ct)).ToHttpResult())
            .WithSummary("checklists.complete (assignee)");

        // Time.
        tasks.MapGet("/time", async (Guid id, TimeService time, CancellationToken ct) => (await time.TaskTimeAsync(id, ct)).ToHttpResult())
            .WithSummary("time.view — sessions, adjustments and totals per revision and member.");
        tasks.MapPost("/time-adjustments", async (Guid id, AddTimeAdjustmentCommand command, TimeService time, CancellationToken ct) =>
                (await time.AddAdjustmentAsync(id, command, ct)).ToHttpResult())
            .WithSummary("time.adjust — minutes (+/−) with a reason.");

        api.MapGet("/me/active-session", async Task<IResult> (ICurrentUser currentUser, WorkService work, CancellationToken ct) =>
                await work.ActiveSessionAsync(currentUser.RequiredUserId, ct) is { } session ? TypedResults.Ok(session) : TypedResults.NoContent())
            .WithTags("Work")
            .RequireAuthorization()
            .WithSummary("The caller's running work session for the timer bar, or 204 when none.");

        var templates = api.MapGroup("/checklist-templates").WithTags("Checklists").RequireAuthorization();
        templates.MapGet("/", async (Guid? projectId, ChecklistTemplates service, CancellationToken ct) => (await service.ListAsync(projectId, ct)).ToHttpResult())
            .WithSummary("Global templates (checklists.manage), or with projectId the templates usable in that project.");
        templates.MapPost("/", async Task<IResult> (SaveChecklistTemplateCommand command, ChecklistTemplates service, CancellationToken ct) =>
            {
                var result = await service.CreateAsync(command, ct);
                return result.IsSuccess ? TypedResults.Created($"/api/v1/checklist-templates/{result.Value.Id}", result.Value) : result.Error!.ToProblem();
            })
            .WithSummary("checklists.manage (global) or checklists.manage-project (with projectId).");
        templates.MapPatch("/{id:guid}", async (Guid id, SaveChecklistTemplateCommand command, ChecklistTemplates service, CancellationToken ct) =>
                (await service.UpdateAsync(id, command, ct)).ToHttpResult())
            .WithSummary("Same permission as creating; replaces the items. Existing task checklists are not changed.");

        return api;
    }
}
