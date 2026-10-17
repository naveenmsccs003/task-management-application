using TaskManagement.Api.Common;
using TaskManagement.Application.Delays;
using TaskManagement.Application.Requests;
using TaskManagement.Application.Review;
using TaskManagement.Domain.Tasks;

namespace TaskManagement.Api.Endpoints;

/// <summary>
/// Review and closure, reassignment and extension requests, and delay reports (docs/api-design.md §4.5, §4.8). Permissions
/// depend on the caller's role in the task's project and on who is the assignee or requester, so the handlers check them.
/// </summary>
internal static class ReviewEndpoints
{
    public static IEndpointRouteBuilder MapReviewEndpoints(this IEndpointRouteBuilder api)
    {
        var tasks = api.MapGroup("/tasks/{id:guid}").WithTags("Review").RequireAuthorization();

        tasks.MapPost("/submit-review", async (Guid id, SubmitReviewCommand command, ReviewService review, CancellationToken ct) =>
                (await review.SubmitAsync(id, command, ct)).ToHttpResult())
            .WithSummary("tasks.submit (assignee) — T10; every required checklist item must be done.");
        tasks.MapPost("/approve", async (Guid id, ApproveReviewCommand? command, ReviewService review, CancellationToken ct) =>
                (await review.ApproveAsync(id, command ?? new ApproveReviewCommand(null), ct)).ToHttpResult())
            .WithSummary("tasks.review — T11; not by the assignee.");
        tasks.MapPost("/request-revision", async (Guid id, RequestRevisionCommand command, ReviewService review, CancellationToken ct) =>
                (await review.RequestRevisionAsync(id, command, ct)).ToHttpResult())
            .WithSummary("tasks.review — T12; comment required.");
        tasks.MapPost("/close", async (Guid id, CloseTaskCommand? command, ReviewService review, CancellationToken ct) =>
                (await review.CloseAsync(id, command ?? new CloseTaskCommand(null), ct)).ToHttpResult())
            .WithSummary("tasks.close — T13; needs approval of the current revision.");
        tasks.MapPost("/reopen", async (Guid id, ReopenTaskCommand command, ReviewService review, CancellationToken ct) =>
                (await review.ReopenAsync(id, command, ct)).ToHttpResult())
            .WithSummary("tasks.reopen — T14; reason required, starts a new revision.");
        tasks.MapGet("/reviews", async (Guid id, ReviewService review, CancellationToken ct) => (await review.ListAsync(id, ct)).ToHttpResult())
            .WithSummary("tasks.view — review submissions and decisions, newest first.");

        tasks.MapGet("/requests", async (Guid id, RequestService requests, CancellationToken ct) => (await requests.ListForTaskAsync(id, ct)).ToHttpResult())
            .WithSummary("tasks.view");
        tasks.MapPost("/reassignment-requests", async (Guid id, RequestReassignmentCommand command, RequestService requests, CancellationToken ct) =>
                (await requests.RequestReassignmentAsync(id, command, ct)).ToHttpResult())
            .WithSummary("tasks.assign — reason of at least 10 characters.");
        tasks.MapPost("/extension-requests", async (Guid id, RequestExtensionCommand command, RequestService requests, CancellationToken ct) =>
                (await requests.RequestExtensionAsync(id, command, ct)).ToHttpResult())
            .WithSummary("deadlines.request (assignee, or TL/PM).");

        tasks.MapGet("/delay-reports", async (Guid id, DelayService delays, CancellationToken ct) => (await delays.ListAsync(id, ct)).ToHttpResult())
            .WithSummary("delays.view");
        tasks.MapPost("/delay-reports", async (Guid id, ReportDelayCommand command, DelayService delays, CancellationToken ct) =>
                (await delays.ReportAsync(id, command, ct)).ToHttpResult())
            .WithSummary("delays.report (assignee).");

        var requestsGroup = api.MapGroup("/requests").WithTags("Requests").RequireAuthorization();
        requestsGroup.MapGet("/", async (ApprovalRequestStatus? status, Guid? projectId, RequestService requests, CancellationToken ct) =>
                TypedResults.Ok(await requests.QueueAsync(status, projectId, ct)))
            .WithSummary("The caller's approval queue (reassignments.approve / deadlines.approve).");
        requestsGroup.MapPost("/{requestId:guid}/approve", async (Guid requestId, DecideRequestCommand? command, RequestService requests, CancellationToken ct) =>
                (await requests.ApproveAsync(requestId, command ?? new DecideRequestCommand(null, null), ct)).ToHttpResult())
            .WithSummary("reassignments.approve / deadlines.approve — not the requester; a TL's request needs a PM and vice versa (or an Admin).");
        requestsGroup.MapPost("/{requestId:guid}/reject", async (Guid requestId, DecideRequestCommand command, RequestService requests, CancellationToken ct) =>
                (await requests.RejectAsync(requestId, command, ct)).ToHttpResult())
            .WithSummary("Same as approve; comment required.");
        requestsGroup.MapPost("/{requestId:guid}/withdraw", async (Guid requestId, RequestService requests, CancellationToken ct) =>
                (await requests.WithdrawAsync(requestId, ct)).ToHttpResult())
            .WithSummary("The requester only.");

        return api;
    }
}
