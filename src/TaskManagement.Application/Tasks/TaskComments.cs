using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Attachments;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Common;
using TaskManagement.Domain.Files;
using TaskManagement.Domain.Projects;
using TaskManagement.Domain.Tasks;

namespace TaskManagement.Application.Tasks;

/// <summary>
/// Task comments (FR-TASK-08): anyone who can see the task can read them; tasks.comment is needed to post. Authors edit
/// within 15 minutes (edits keep history); the author or a TL/PM (tasks.update in the project) can delete, softly.
/// </summary>
public sealed class TaskComments(
    IAppDbContext db, ICurrentUser currentUser, TaskAccess access, AttachmentService attachments, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<IReadOnlyList<TaskCommentDto>>> ListAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var visible = await access.LoadAsync(taskId, track: false, cancellationToken);
        if (visible is null)
        {
            return CommonErrors.NotFound("Task");
        }

        var comments = await db.TaskComments.AsNoTracking()
            .Where(c => c.TaskId == taskId)
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);
        var authorIds = comments.Select(c => c.AuthorId).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => authorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new UserRef(u.Id, u.FullName), cancellationToken);

        IReadOnlyList<TaskCommentDto> result = comments.Select(c => ToDto(c, visible, names.GetValueOrDefault(c.AuthorId))).ToList();
        return Result.Success(result);
    }

    public async Task<Result<TaskCommentDto>> AddAsync(Guid taskId, SaveCommentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var visible = await access.LoadAsync(taskId, track: false, cancellationToken);
        if (visible is null)
        {
            return CommonErrors.NotFound("Task");
        }

        if (!TaskAccess.CanComment(visible))
        {
            return CommonErrors.Forbidden("comment on this task");
        }

        var writable = EnsureWritable(visible);
        if (writable.IsFailure)
        {
            return writable.Error!;
        }

        var errors = ValidateBody(command.Body);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var comment = TaskComment.Create(taskId, currentUser.RequiredUserId, command.Body!, timeProvider.GetUtcNow());
        db.TaskComments.Add(comment);
        var linked = await attachments.LinkDraftsAsync(taskId, AttachmentOwnerType.Comment, comment.Id, command.AttachmentIds, comment.AuthorId, cancellationToken);
        if (linked.IsFailure)
        {
            return linked.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToDto(comment, visible, await AuthorAsync(comment.AuthorId, cancellationToken));
    }

    public async Task<Result<TaskCommentDto>> EditAsync(Guid taskId, Guid commentId, SaveCommentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var visible = await access.LoadAsync(taskId, track: false, cancellationToken);
        if (visible is null)
        {
            return CommonErrors.NotFound("Task");
        }

        var comment = await db.TaskComments.FirstOrDefaultAsync(c => c.Id == commentId && c.TaskId == taskId, cancellationToken);
        if (comment is null)
        {
            return CommonErrors.NotFound("Comment");
        }

        var writable = EnsureWritable(visible);
        if (writable.IsFailure)
        {
            return writable.Error!;
        }

        var errors = ValidateBody(command.Body);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var result = comment.Edit(command.Body!, currentUser.RequiredUserId, timeProvider.GetUtcNow());
        if (result.IsFailure)
        {
            return result.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToDto(comment, visible, await AuthorAsync(comment.AuthorId, cancellationToken));
    }

    public async Task<Result> DeleteAsync(Guid taskId, Guid commentId, CancellationToken cancellationToken)
    {
        var visible = await access.LoadAsync(taskId, track: false, cancellationToken);
        if (visible is null)
        {
            return CommonErrors.NotFound("Task");
        }

        var comment = await db.TaskComments.FirstOrDefaultAsync(c => c.Id == commentId && c.TaskId == taskId, cancellationToken);
        if (comment is null)
        {
            return CommonErrors.NotFound("Comment");
        }

        if (!CanModerate(comment, visible))
        {
            return CommonErrors.Forbidden("delete this comment");
        }

        if (visible.Project.IsReadOnly)
        {
            return Project.ReadOnlyError();
        }

        if (!comment.IsDeleted)
        {
            comment.Delete(currentUser.RequiredUserId, timeProvider.GetUtcNow());
            await attachments.DeleteForOwnerAsync(AttachmentOwnerType.Comment, comment.Id, currentUser.RequiredUserId, cancellationToken);
            auditLog.Record("task.comment_deleted", "TaskComment", comment.Id.ToString(), new AuditChanges()
                .Set("taskId", taskId)
                .Set("authorId", comment.AuthorId), visible.Task.ProjectId);
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    private TaskCommentDto ToDto(TaskComment comment, VisibleTask visible, UserRef? author)
    {
        var userId = currentUser.RequiredUserId;
        var writable = EnsureWritable(visible).IsSuccess;
        return new TaskCommentDto(
            comment.Id,
            author,
            comment.IsDeleted ? null : comment.Body,
            comment.CreatedAt,
            comment.EditedAt,
            comment.IsDeleted,
            writable && comment.CanBeEditedBy(userId, timeProvider.GetUtcNow()),
            !visible.Project.IsReadOnly && !comment.IsDeleted && CanModerate(comment, visible));
    }

    private bool CanModerate(TaskComment comment, VisibleTask visible) =>
        comment.AuthorId == currentUser.UserId || visible.Permissions.HasProjectWide(PermissionCatalog.TasksUpdate);

    /// <summary>Comments are part of the record: read-only projects and closed or cancelled tasks take no new or edited comments.</summary>
    private static Result EnsureWritable(VisibleTask visible)
    {
        if (visible.Project.IsReadOnly)
        {
            return Project.ReadOnlyError();
        }

        return TaskWorkflow.IsFinal(visible.Task.Status)
            ? Error.Conflict("task.read_only", $"Task {visible.Task.Key} is {TaskWorkflow.Describe(visible.Task.Status).ToLowerInvariant()}, so comments are closed.")
            : Result.Success();
    }

    private static FieldErrors ValidateBody(string? body) =>
        new FieldErrors()
            .Required("body", body, "Comment")
            .MaxLength("body", body?.Trim(), TaskComment.BodyMaxLength, "Comment");

    private Task<UserRef?> AuthorAsync(Guid authorId, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().Where(u => u.Id == authorId).Select(u => new UserRef(u.Id, u.FullName)).FirstOrDefaultAsync(cancellationToken);
}
