using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Attachments;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Application.Notifications;
using TaskManagement.Application.Tasks;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Common;
using TaskManagement.Domain.Files;
using TaskManagement.Domain.Notifications;
using TaskManagement.Domain.Projects;
using TaskManagement.Domain.Tasks;

namespace TaskManagement.Application.Algorithms;

public sealed record AlgorithmSubmissionDto(
    Guid Id,
    int Version,
    int Revision,
    string Content,
    UserRef? SubmittedBy,
    DateTimeOffset SubmittedAt,
    AlgorithmDecision Decision,
    UserRef? DecidedBy,
    DateTimeOffset? DecidedAt,
    string? DecisionComment);

/// <summary><paramref name="AttachmentIds"/>: the caller's draft files uploaded with owner type Algorithm (FR-ALG-02).</summary>
public sealed record SubmitAlgorithmCommand(string? Content, IReadOnlyList<Guid>? AttachmentIds = null);

public sealed record AlgorithmDecisionCommand(string? Comment);

/// <summary>
/// Algorithm submission and TL approval (T3–T5, FR-ALG-01..05). Versions are immutable; every version and decision is
/// kept. Nobody decides on their own algorithm, and a TL who is the assignee cannot approve it either (AC-ALG-3).
/// </summary>
public sealed class AlgorithmService(
    IAppDbContext db, TaskAccess access, TaskQueries queries, AttachmentService attachments, Notifier notifier, IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public async Task<Result<IReadOnlyList<AlgorithmSubmissionDto>>> ListAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var visible = await access.LoadAsync(taskId, track: false, cancellationToken);
        if (visible is null)
        {
            return CommonErrors.NotFound("Task");
        }

        if (!visible.Allows(PermissionCatalog.AlgorithmsView))
        {
            return CommonErrors.Forbidden("view this task's algorithm");
        }

        var submissions = await db.AlgorithmSubmissions.AsNoTracking()
            .Where(a => a.TaskId == taskId)
            .OrderByDescending(a => a.Version)
            .ToListAsync(cancellationToken);
        var userIds = submissions.Select(a => a.SubmittedBy)
            .Concat(submissions.Where(a => a.DecidedBy is not null).Select(a => a.DecidedBy!.Value))
            .Distinct()
            .ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new UserRef(u.Id, u.FullName), cancellationToken);

        IReadOnlyList<AlgorithmSubmissionDto> result = submissions
            .Select(a => new AlgorithmSubmissionDto(
                a.Id, a.Version, a.Revision, a.Content, names.GetValueOrDefault(a.SubmittedBy), a.SubmittedAt, a.Decision,
                a.DecidedBy is { } decidedBy ? names.GetValueOrDefault(decidedBy) : null, a.DecidedAt, a.DecisionComment))
            .ToList();
        return Result.Success(result);
    }

    public async Task<Result<TaskDto>> SubmitAsync(Guid taskId, SubmitAlgorithmCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var visible = await access.LoadAsync(taskId, track: true, cancellationToken);
        if (visible is null)
        {
            return CommonErrors.NotFound("Task");
        }

        if (!visible.IsAssignee || !visible.Allows(PermissionCatalog.AlgorithmsSubmit))
        {
            return Error.Forbidden("algorithm.not_assignee", "Only the task's assignee can submit its algorithm.");
        }

        if (visible.Project.IsReadOnly)
        {
            return Project.ReadOnlyError();
        }

        var errors = new FieldErrors()
            .Required("content", command.Content, "The algorithm")
            .MaxLength("content", command.Content?.Trim(), AlgorithmSubmission.ContentMaxLength, "The algorithm");
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var task = visible.Task;
        var now = timeProvider.GetUtcNow();
        var previousStatus = task.Status;
        var result = task.SubmitAlgorithm(visible.UserId, now);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        var version = await db.AlgorithmSubmissions.Where(a => a.TaskId == taskId).MaxAsync(a => (int?)a.Version, cancellationToken) ?? 0;
        var submission = AlgorithmSubmission.Create(taskId, task.CurrentRevision, version + 1, command.Content!, visible.UserId, now);
        db.AlgorithmSubmissions.Add(submission);
        var linked = await attachments.LinkDraftsAsync(taskId, AttachmentOwnerType.Algorithm, submission.Id, command.AttachmentIds, visible.UserId, cancellationToken);
        if (linked.IsFailure)
        {
            return linked.Error!;
        }

        await notifier.NotifyAsync(
            await notifier.ProjectLeadsAsync(task.ProjectId, cancellationToken), NotificationType.AlgorithmSubmitted,
            $"Algorithm submitted for {task.Key}", $"Version {submission.Version} of the algorithm for “{task.Title}” is waiting for review.",
            $"/tasks/{taskId}", task.ProjectId, taskId, cancellationToken);
        auditLog.Record("algorithm.submitted", "AlgorithmSubmission", submission.Id.ToString(), new AuditChanges()
            .Set("taskId", taskId)
            .Set("version", submission.Version)
            .Change("status", previousStatus, task.Status), task.ProjectId);
        await db.SaveChangesAsync(cancellationToken);
        return await queries.GetAsync(taskId, cancellationToken);
    }

    public Task<Result<TaskDto>> ApproveAsync(Guid taskId, Guid submissionId, AlgorithmDecisionCommand command, CancellationToken cancellationToken) =>
        DecideAsync(taskId, submissionId, command, approve: true, cancellationToken);

    public Task<Result<TaskDto>> RejectAsync(Guid taskId, Guid submissionId, AlgorithmDecisionCommand command, CancellationToken cancellationToken) =>
        DecideAsync(taskId, submissionId, command, approve: false, cancellationToken);

    private async Task<Result<TaskDto>> DecideAsync(
        Guid taskId, Guid submissionId, AlgorithmDecisionCommand command, bool approve, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var visible = await access.LoadAsync(taskId, track: true, cancellationToken);
        if (visible is null)
        {
            return CommonErrors.NotFound("Task");
        }

        if (!visible.Permissions.HasProjectWide(PermissionCatalog.AlgorithmsApprove))
        {
            return CommonErrors.Forbidden("approve or reject algorithms in this project");
        }

        var submission = await db.AlgorithmSubmissions.FirstOrDefaultAsync(a => a.Id == submissionId && a.TaskId == taskId, cancellationToken);
        if (submission is null)
        {
            return CommonErrors.NotFound("Algorithm");
        }

        // FR-ALG-05: a TL who is the assignee needs another TL, even if someone else typed the submission.
        if (visible.IsAssignee)
        {
            return Error.Forbidden("algorithm.own_submission", "You cannot decide on the algorithm of a task assigned to you. Another Team Leader must review it.");
        }

        if (visible.Project.IsReadOnly)
        {
            return Project.ReadOnlyError();
        }

        var errors = new FieldErrors().MaxLength("comment", command.Comment?.Trim(), AlgorithmSubmission.CommentMaxLength, "The comment");
        if (!approve)
        {
            errors.Required("comment", command.Comment, "A comment explaining the rejection");
        }

        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var task = visible.Task;
        var now = timeProvider.GetUtcNow();
        var previousStatus = task.Status;
        var decided = approve ? submission.Approve(visible.UserId, command.Comment, now) : submission.Reject(visible.UserId, command.Comment!, now);
        if (decided.IsFailure)
        {
            return decided.Error!;
        }

        var moved = approve
            ? task.ApproveAlgorithm(submission.Id, visible.UserId, submission.DecisionComment, now)
            : task.RejectAlgorithm(visible.UserId, submission.DecisionComment!, now);
        if (moved.IsFailure)
        {
            return moved.Error!;
        }

        if (task.AssigneeId is { } assigneeId)
        {
            await notifier.NotifyAsync(
                [assigneeId],
                approve ? NotificationType.AlgorithmApproved : NotificationType.AlgorithmRejected,
                approve ? $"Algorithm approved for {task.Key}" : $"Algorithm rejected for {task.Key}",
                approve
                    ? $"Version {submission.Version} was approved. You can start work on “{task.Title}”."
                    : $"Version {submission.Version} was rejected: {submission.DecisionComment}",
                $"/tasks/{taskId}", task.ProjectId, taskId, cancellationToken);
        }

        auditLog.Record(approve ? "algorithm.approved" : "algorithm.rejected", "AlgorithmSubmission", submission.Id.ToString(), new AuditChanges()
            .Set("taskId", taskId)
            .Set("version", submission.Version)
            .Set("comment", submission.DecisionComment)
            .Change("status", previousStatus, task.Status), task.ProjectId);
        await db.SaveChangesAsync(cancellationToken);
        return await queries.GetAsync(taskId, cancellationToken);
    }
}
