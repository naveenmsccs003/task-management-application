using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Application.Notifications;
using TaskManagement.Application.Requests;
using TaskManagement.Application.Tasks;
using TaskManagement.Application.Work;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Common;
using TaskManagement.Domain.Notifications;
using TaskManagement.Domain.Projects;
using TaskManagement.Domain.Tasks;
using TaskManagement.Domain.Time;

namespace TaskManagement.Application.Review;

public sealed record SubmitReviewCommand(string? Summary);

public sealed record ApproveReviewCommand(string? Comment);

public sealed record RequestRevisionCommand(string? Comment, IReadOnlyList<Guid>? ReopenChecklistItemIds, bool AlgorithmReapprovalRequired);

public sealed record CloseTaskCommand(string? Comment);

public sealed record ReopenTaskCommand(string? Reason);

public sealed record ReviewSubmissionDto(
    Guid Id, int Revision, string Summary, UserRef? SubmittedBy, DateTimeOffset SubmittedAt, ReviewDecision Decision,
    UserRef? ReviewedBy, DateTimeOffset? ReviewedAt, string? Comment, bool AlgorithmReapprovalRequired);

/// <summary>
/// Submit for review, TL review, closure and reopening (T10–T14, FR-RVW-01..05, FR-CHK-05/06, FR-REV-01, FR-ALG-06).
/// Nobody reviews work they are assigned to (FR-RVW-03).
/// </summary>
public sealed class ReviewService(
    IAppDbContext db,
    TaskAccess access,
    TaskQueries queries,
    WorkService work,
    RequestService requests,
    Notifier notifier,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public async Task<Result<IReadOnlyList<ReviewSubmissionDto>>> ListAsync(Guid taskId, CancellationToken cancellationToken)
    {
        if (await access.LoadAsync(taskId, track: false, cancellationToken) is null)
        {
            return CommonErrors.NotFound("Task");
        }

        var reviews = await db.ReviewSubmissions.AsNoTracking().Where(r => r.TaskId == taskId).OrderByDescending(r => r.SubmittedAt).ToListAsync(cancellationToken);
        var ids = reviews.Select(r => r.SubmittedBy).Concat(reviews.Where(r => r.ReviewedBy != null).Select(r => r.ReviewedBy!.Value)).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => new UserRef(u.Id, u.FullName), cancellationToken);
        IReadOnlyList<ReviewSubmissionDto> result = reviews
            .Select(r => new ReviewSubmissionDto(
                r.Id, r.Revision, r.Summary, names.GetValueOrDefault(r.SubmittedBy), r.SubmittedAt, r.Decision,
                r.ReviewedBy is { } by ? names.GetValueOrDefault(by) : null, r.ReviewedAt, r.Comment, r.AlgorithmReapprovalRequired))
            .ToList();
        return Result.Success(result);
    }

    /// <summary>T10: every mandatory checklist item must be done (AC-CHK-1) and no reassignment may be pending.</summary>
    public async Task<Result<TaskDto>> SubmitAsync(Guid taskId, SubmitReviewCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var guard = await LoadAsync(taskId, cancellationToken);
        if (guard.IsFailure)
        {
            return guard.Error!;
        }

        var visible = guard.Value;
        if (!visible.IsAssignee || !visible.Allows(PermissionCatalog.TasksSubmit))
        {
            return Error.Forbidden("review.not_assignee", "Only the task's assignee can submit it for review.");
        }

        var errors = new FieldErrors()
            .Required("summary", command.Summary, "A summary of the work")
            .MaxLength("summary", command.Summary?.Trim(), ReviewSubmission.SummaryMaxLength, "The summary");
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var task = visible.Task;
        var missing = await db.TaskChecklistItems.AsNoTracking()
            .Where(i => i.TaskId == taskId && i.IsMandatory && !i.IsCompleted)
            .OrderBy(i => i.SortOrder)
            .Select(i => i.Text)
            .ToListAsync(cancellationToken);
        if (missing.Count > 0)
        {
            return Error.Conflict("review.checklist_incomplete", $"Complete the required checklist items first: {string.Join("; ", missing)}.");
        }

        if (await db.ApprovalRequests.AnyAsync(r => r.TaskId == taskId && r.Type == ApprovalRequestType.Reassignment && r.Status == ApprovalRequestStatus.Pending, cancellationToken))
        {
            return Error.Conflict("review.reassignment_pending", "A reassignment of this task is waiting for approval. It must be decided before review.");
        }

        var now = timeProvider.GetUtcNow();
        var previous = task.Status;
        var result = task.SubmitReview(visible.UserId, now);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        await work.CloseActiveSessionAsync(task.Id, SessionEndReason.Submitted, now, cancellationToken);
        db.ReviewSubmissions.Add(ReviewSubmission.Create(task.Id, task.CurrentRevision, command.Summary!, visible.UserId, now));
        await notifier.NotifyAsync(
            await LeadsExcept(task, cancellationToken), NotificationType.SubmittedForReview, $"{task.Key} is ready for review",
            $"“{task.Title}” (revision {task.CurrentRevision}) was submitted for review.", Link(task), task.ProjectId, task.Id, cancellationToken);
        Audit(task, "task.submitted_for_review", previous);
        await db.SaveChangesAsync(cancellationToken);
        return await queries.GetAsync(task.Id, cancellationToken);
    }

    /// <summary>T11; with the project's "close on approval" option the task closes straight away.</summary>
    public async Task<Result<TaskDto>> ApproveAsync(Guid taskId, ApproveReviewCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var guard = await LoadForReviewerAsync(taskId, cancellationToken);
        if (guard.IsFailure)
        {
            return guard.Error!;
        }

        var (visible, review) = guard.Value;
        var errors = new FieldErrors().MaxLength("comment", command.Comment?.Trim(), ReviewSubmission.CommentMaxLength, "The comment");
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var task = visible.Task;
        var now = timeProvider.GetUtcNow();
        var previous = task.Status;
        var result = task.ApproveReview(visible.UserId, command.Comment, visible.Project.CloseOnApproval, now);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        review.Approve(visible.UserId, command.Comment, now);
        if (task.Status == TaskItemStatus.Closed)
        {
            await requests.AutoCancelPendingAsync(task.Id, "The task was closed.", cancellationToken);
        }

        var managers = await ProjectRoleMembersAsync(task.ProjectId, SystemRoles.ProjectManagerId, cancellationToken);
        await notifier.NotifyAsync(
            managers.Append(task.AssigneeId!.Value), NotificationType.ReviewApproved,
            task.Status == TaskItemStatus.Closed ? $"{task.Key} was approved and closed" : $"{task.Key} was approved",
            $"Revision {task.CurrentRevision} of “{task.Title}” was approved.{(string.IsNullOrWhiteSpace(command.Comment) ? string.Empty : $" Comment: {command.Comment.Trim()}")}",
            Link(task), task.ProjectId, task.Id, cancellationToken);
        Audit(task, "task.review_approved", previous);
        await db.SaveChangesAsync(cancellationToken);
        return await queries.GetAsync(task.Id, cancellationToken);
    }

    /// <summary>T12: comment required (AC-RVW-1); the revision goes up (AC-RVW-2); chosen checklist items reopen.</summary>
    public async Task<Result<TaskDto>> RequestRevisionAsync(Guid taskId, RequestRevisionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var guard = await LoadForReviewerAsync(taskId, cancellationToken);
        if (guard.IsFailure)
        {
            return guard.Error!;
        }

        var (visible, review) = guard.Value;
        var task = visible.Task;
        var errors = new FieldErrors()
            .Required("comment", command.Comment, "A comment explaining what to change")
            .MaxLength("comment", command.Comment?.Trim(), ReviewSubmission.CommentMaxLength, "The comment");
        var reopenIds = command.ReopenChecklistItemIds ?? [];
        var items = await db.TaskChecklistItems.Where(i => i.TaskId == taskId && reopenIds.Contains(i.Id)).ToListAsync(cancellationToken);
        if (items.Count != reopenIds.Distinct().Count())
        {
            errors.Add("reopenChecklistItemIds", "Choose items from this task's checklist.");
        }

        if (command.AlgorithmReapprovalRequired && !task.AlgorithmRequired)
        {
            errors.Add("algorithmReapprovalRequired", "This task does not use an algorithm. Turn on \"algorithm required\" first if a new one is needed.");
        }

        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var now = timeProvider.GetUtcNow();
        var previous = task.Status;
        var result = task.RequestRevision(visible.UserId, command.Comment!, command.AlgorithmReapprovalRequired, now);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        review.RequestRevision(visible.UserId, command.Comment!, command.AlgorithmReapprovalRequired, now);
        foreach (var item in items)
        {
            item.Reopen(task.CurrentRevision);
        }

        await notifier.NotifyAsync(
            [task.AssigneeId!.Value], NotificationType.RevisionRequested, $"Changes requested on {task.Key}",
            $"Revision {task.CurrentRevision} of “{task.Title}” is needed: {command.Comment!.Trim()}", Link(task), task.ProjectId, task.Id, cancellationToken);
        Audit(task, "task.revision_requested", previous, new AuditChanges()
            .Set("revision", task.CurrentRevision)
            .Set("reopenedChecklistItems", items.Count)
            .Set("algorithmReapprovalRequired", command.AlgorithmReapprovalRequired));
        await db.SaveChangesAsync(cancellationToken);
        return await queries.GetAsync(task.Id, cancellationToken);
    }

    /// <summary>T13: closing needs the current revision approved (AC-RVW-3); pending requests are cancelled (edge case 26).</summary>
    public async Task<Result<TaskDto>> CloseAsync(Guid taskId, CloseTaskCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var guard = await LoadAsync(taskId, cancellationToken);
        if (guard.IsFailure)
        {
            return guard.Error!;
        }

        var visible = guard.Value;
        if (!visible.Permissions.HasProjectWide(PermissionCatalog.TasksClose))
        {
            return CommonErrors.Forbidden("close this task");
        }

        var task = visible.Task;
        var now = timeProvider.GetUtcNow();
        var previous = task.Status;
        var result = task.Close(visible.UserId, command.Comment, now);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        await requests.AutoCancelPendingAsync(task.Id, "The task was closed.", cancellationToken);
        var recipients = (await notifier.ProjectLeadsAsync(task.ProjectId, cancellationToken)).Concat(task.AssigneeId is { } a ? [a] : []);
        await notifier.NotifyAsync(
            recipients, NotificationType.TaskClosed, $"{task.Key} was closed", $"“{task.Title}” is closed.", Link(task), task.ProjectId, task.Id, cancellationToken);
        Audit(task, "task.closed", previous);
        await db.SaveChangesAsync(cancellationToken);
        return await queries.GetAsync(task.Id, cancellationToken);
    }

    /// <summary>T14: reason required; a new revision starts (FR-RVW-05).</summary>
    public async Task<Result<TaskDto>> ReopenAsync(Guid taskId, ReopenTaskCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var guard = await LoadAsync(taskId, cancellationToken);
        if (guard.IsFailure)
        {
            return guard.Error!;
        }

        var visible = guard.Value;
        if (!visible.Permissions.HasProjectWide(PermissionCatalog.TasksReopen))
        {
            return CommonErrors.Forbidden("reopen this task");
        }

        var errors = new FieldErrors()
            .Required("reason", command.Reason, "A reason")
            .MaxLength("reason", command.Reason?.Trim(), TaskItem.ReasonMaxLength, "The reason");
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var task = visible.Task;
        var previous = task.Status;
        var result = task.Reopen(visible.UserId, command.Reason!, timeProvider.GetUtcNow());
        if (result.IsFailure)
        {
            return result.Error!;
        }

        var recipients = (await notifier.ProjectLeadsAsync(task.ProjectId, cancellationToken)).Concat(task.AssigneeId is { } a ? [a] : []);
        await notifier.NotifyAsync(
            recipients, NotificationType.TaskReopened, $"{task.Key} was reopened",
            $"“{task.Title}” was reopened as revision {task.CurrentRevision}: {command.Reason!.Trim()}", Link(task), task.ProjectId, task.Id, cancellationToken);
        Audit(task, "task.reopened", previous, new AuditChanges().Set("reason", command.Reason!.Trim()).Set("revision", task.CurrentRevision));
        await db.SaveChangesAsync(cancellationToken);
        return await queries.GetAsync(task.Id, cancellationToken);
    }

    private async Task<Result<VisibleTask>> LoadAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var visible = await access.LoadAsync(taskId, track: true, cancellationToken);
        if (visible is null)
        {
            return CommonErrors.NotFound("Task");
        }

        return visible.Project.IsReadOnly ? Project.ReadOnlyError() : visible;
    }

    /// <summary>tasks.review in the project, not the assignee (FR-RVW-03), and a pending review for the current revision.</summary>
    private async Task<Result<(VisibleTask Visible, ReviewSubmission Review)>> LoadForReviewerAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var guard = await LoadAsync(taskId, cancellationToken);
        if (guard.IsFailure)
        {
            return guard.Error!;
        }

        var visible = guard.Value;
        if (!visible.Permissions.HasProjectWide(PermissionCatalog.TasksReview))
        {
            return CommonErrors.Forbidden("review this task");
        }

        if (visible.IsAssignee)
        {
            return Error.Forbidden("review.own_work", "You cannot review a task assigned to you. Another Team Leader must review it.");
        }

        var task = visible.Task;
        var review = await db.ReviewSubmissions
            .Where(r => r.TaskId == taskId && r.Revision == task.CurrentRevision && r.Decision == ReviewDecision.Pending)
            .OrderByDescending(r => r.SubmittedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (review is null || task.Status != TaskItemStatus.InReview)
        {
            return TaskWorkflow.InvalidTransition(task.Key, task.Status, TaskAction.ApproveReview);
        }

        return (visible, review);
    }

    private async Task<IEnumerable<Guid>> LeadsExcept(TaskItem task, CancellationToken cancellationToken) =>
        (await notifier.ProjectLeadsAsync(task.ProjectId, cancellationToken)).Where(id => id != task.AssigneeId);

    private Task<List<Guid>> ProjectRoleMembersAsync(Guid projectId, Guid roleId, CancellationToken cancellationToken) =>
        db.ProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.RemovedAt == null && m.ProjectRoleId == roleId)
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken);

    private void Audit(TaskItem task, string action, TaskItemStatus previous, AuditChanges? extra = null) =>
        auditLog.Record(action, "Task", task.Id.ToString(), (extra ?? new AuditChanges()).Change("status", previous, task.Status), task.ProjectId);

    private static string Link(TaskItem task) => $"/tasks/{task.Id}";
}
