using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Application.Tasks;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Checklists;
using TaskManagement.Domain.Common;
using TaskManagement.Domain.Projects;
using TaskManagement.Domain.Tasks;

namespace TaskManagement.Application.Checklists;

public sealed record TaskChecklistItemDto(
    Guid Id,
    int SortOrder,
    string Text,
    bool IsMandatory,
    bool FromTemplate,
    bool IsCompleted,
    UserRef? CompletedBy,
    DateTimeOffset? CompletedAt,
    string? Note,
    uint Version);

public sealed record AddChecklistItemCommand(string? Text, bool IsMandatory);

public sealed record CompleteChecklistItemCommand(string? Note);

/// <summary>
/// A task's checklist (FR-CHK-02..04): TL/PM add task-specific items before review; the assignee ticks items while
/// working. The "all mandatory items done" rule is checked when the task is submitted for review (FR-CHK-05).
/// </summary>
public sealed class TaskChecklist(IAppDbContext db, TaskAccess access, IAuditLog auditLog, TimeProvider timeProvider)
{
    /// <summary>States in which the assignee can tick items: assigned and allowed to work, not yet submitted.</summary>
    public static bool CanTick(TaskItemStatus status) =>
        status is TaskItemStatus.ReadyToStart or TaskItemStatus.InProgress or TaskItemStatus.Paused
            or TaskItemStatus.Stopped or TaskItemStatus.RevisionRequested;

    /// <summary>Items can be added until the task is submitted for review.</summary>
    public static bool CanAddItems(TaskItemStatus status) =>
        !TaskWorkflow.IsFinal(status) && status is not (TaskItemStatus.InReview or TaskItemStatus.Approved);

    public async Task<Result<IReadOnlyList<TaskChecklistItemDto>>> ListAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var visible = await access.LoadAsync(taskId, track: false, cancellationToken);
        return visible is null ? CommonErrors.NotFound("Task") : Result.Success(await ItemsAsync(taskId, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<TaskChecklistItemDto>>> AddItemAsync(Guid taskId, AddChecklistItemCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var visible = await access.LoadAsync(taskId, track: false, cancellationToken);
        if (visible is null)
        {
            return CommonErrors.NotFound("Task");
        }

        if (!visible.Permissions.HasProjectWide(PermissionCatalog.ChecklistsManageProject))
        {
            return CommonErrors.Forbidden("add checklist items to this task");
        }

        if (visible.Project.IsReadOnly)
        {
            return Project.ReadOnlyError();
        }

        if (!CanAddItems(visible.Task.Status))
        {
            return Error.Conflict("checklist.locked", "Checklist items can only be added before the task is submitted for review.");
        }

        var errors = new FieldErrors()
            .Required("text", command.Text, "Text")
            .MaxLength("text", command.Text?.Trim(), ChecklistTemplate.ItemTextMaxLength, "Text");
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var nextOrder = (await db.TaskChecklistItems.Where(i => i.TaskId == taskId).MaxAsync(i => (int?)i.SortOrder, cancellationToken) ?? 0) + 1;
        var item = TaskChecklistItem.TaskSpecific(taskId, new ChecklistItemDraft(command.Text!, command.IsMandatory), nextOrder, timeProvider.GetUtcNow());
        db.TaskChecklistItems.Add(item);
        auditLog.Record("checklist.item_added", "TaskChecklistItem", item.Id.ToString(), new AuditChanges()
            .Set("taskId", taskId)
            .Set("text", item.Text)
            .Set("isMandatory", item.IsMandatory), visible.Task.ProjectId);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(await ItemsAsync(taskId, cancellationToken));
    }

    public Task<Result<IReadOnlyList<TaskChecklistItemDto>>> CompleteAsync(
        Guid taskId, Guid itemId, CompleteChecklistItemCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return TickAsync(taskId, itemId, (item, by, now) => item.Complete(by, command.Note, now), command.Note, cancellationToken);
    }

    public Task<Result<IReadOnlyList<TaskChecklistItemDto>>> UncompleteAsync(Guid taskId, Guid itemId, CancellationToken cancellationToken) =>
        TickAsync(taskId, itemId, (item, _, _) => item.Uncomplete(), null, cancellationToken);

    /// <summary>Copies a template's items to a new task (FR-CHK-02). The caller saves.</summary>
    public void Apply(TaskItem task, ChecklistTemplate template)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(template);
        var now = timeProvider.GetUtcNow();
        var order = 1;
        foreach (var source in template.Items.OrderBy(i => i.SortOrder))
        {
            db.TaskChecklistItems.Add(TaskChecklistItem.FromTemplate(task.Id, source, order++, now));
        }

        task.RecordChecklistTemplate(template.Id);
    }

    private async Task<Result<IReadOnlyList<TaskChecklistItemDto>>> TickAsync(
        Guid taskId,
        Guid itemId,
        Func<TaskChecklistItem, Guid, DateTimeOffset, Result> change,
        string? note,
        CancellationToken cancellationToken)
    {
        var visible = await access.LoadAsync(taskId, track: false, cancellationToken);
        if (visible is null)
        {
            return CommonErrors.NotFound("Task");
        }

        var item = await db.TaskChecklistItems.FirstOrDefaultAsync(i => i.Id == itemId && i.TaskId == taskId, cancellationToken);
        if (item is null)
        {
            return CommonErrors.NotFound("Checklist_item");
        }

        if (!visible.IsAssignee || !visible.Allows(PermissionCatalog.ChecklistsComplete))
        {
            return Error.Forbidden("checklist.not_assignee", "Only the task's assignee can tick checklist items.");
        }

        if (visible.Project.IsReadOnly)
        {
            return Project.ReadOnlyError();
        }

        if (!CanTick(visible.Task.Status))
        {
            return Error.Conflict("checklist.locked", $"Checklist items cannot be changed while the task is {TaskWorkflow.Describe(visible.Task.Status).ToLowerInvariant()}.");
        }

        var errors = new FieldErrors().MaxLength("note", note?.Trim(), TaskChecklistItem.NoteMaxLength, "The note");
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var result = change(item, visible.UserId, timeProvider.GetUtcNow());
        if (result.IsFailure)
        {
            return result.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(await ItemsAsync(taskId, cancellationToken));
    }

    private async Task<IReadOnlyList<TaskChecklistItemDto>> ItemsAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var items = await db.TaskChecklistItems.AsNoTracking().Where(i => i.TaskId == taskId).OrderBy(i => i.SortOrder).ToListAsync(cancellationToken);
        var userIds = items.Where(i => i.CompletedBy is not null).Select(i => i.CompletedBy!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new UserRef(u.Id, u.FullName), cancellationToken);
        return items
            .Select(i => new TaskChecklistItemDto(
                i.Id, i.SortOrder, i.Text, i.IsMandatory, i.SourceTemplateItemId is not null, i.IsCompleted,
                i.CompletedBy is { } by ? names.GetValueOrDefault(by) : null, i.CompletedAt, i.Note, i.Version))
            .ToList();
    }
}
