using TaskManagement.Domain.Common;

namespace TaskManagement.Domain.Tasks;

/// <summary>
/// A comment on a task (FR-TASK-08). The author may edit it for <see cref="EditWindow"/>; every edit keeps the
/// previous text in <see cref="Edits"/>. Deletion is soft: the row stays, the body is hidden from readers.
/// </summary>
public sealed class TaskComment
{
    public const int BodyMaxLength = 10_000;

    public static readonly TimeSpan EditWindow = TimeSpan.FromMinutes(15);

    private readonly List<TaskCommentEdit> _edits = [];

    private TaskComment()
    {
        Body = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid TaskId { get; private set; }

    public Guid AuthorId { get; private set; }

    public string Body { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? EditedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    /// <summary>Only edits added in this unit of work (history is append-only and not loaded with the comment).</summary>
    public IReadOnlyCollection<TaskCommentEdit> Edits => _edits;

    public static TaskComment Create(Guid taskId, Guid authorId, string body, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        return new TaskComment { Id = Guid.CreateVersion7(now), TaskId = taskId, AuthorId = authorId, Body = body.Trim(), CreatedAt = now };
    }

    public bool CanBeEditedBy(Guid userId, DateTimeOffset now) => !IsDeleted && userId == AuthorId && now - CreatedAt <= EditWindow;

    public Result Edit(string body, Guid by, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        if (IsDeleted)
        {
            return Error.Conflict("comment.deleted", "This comment was deleted.");
        }

        if (by != AuthorId)
        {
            return Error.Forbidden("comment.not_author", "Only the author can edit a comment.");
        }

        if (now - CreatedAt > EditWindow)
        {
            return Error.Conflict("comment.edit_window_closed", $"Comments can only be edited for {EditWindow.TotalMinutes:0} minutes after posting.");
        }

        var trimmed = body.Trim();
        if (trimmed == Body)
        {
            return Result.Success();
        }

        _edits.Add(TaskCommentEdit.Create(Id, Body, now));
        Body = trimmed;
        EditedAt = now;
        return Result.Success();
    }

    public void Delete(Guid by, DateTimeOffset now)
    {
        if (IsDeleted)
        {
            return;
        }

        DeletedAt = now;
        DeletedBy = by;
    }
}

/// <summary>The text a comment had before an edit (append-only).</summary>
public sealed class TaskCommentEdit
{
    private TaskCommentEdit()
    {
        PreviousBody = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid CommentId { get; private set; }

    public string PreviousBody { get; private set; }

    public DateTimeOffset EditedAt { get; private set; }

    internal static TaskCommentEdit Create(Guid commentId, string previousBody, DateTimeOffset now) =>
        new() { Id = Guid.CreateVersion7(now), CommentId = commentId, PreviousBody = previousBody, EditedAt = now };
}
