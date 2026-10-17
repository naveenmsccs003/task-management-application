using TaskManagement.Domain.Common;

namespace TaskManagement.Domain.Checklists;

/// <summary>The text and mandatory flag of one checklist line, as entered by a manager.</summary>
public sealed record ChecklistItemDraft(string Text, bool IsMandatory);

/// <summary>
/// A reusable checklist (FR-CHK-01): global when <see cref="ProjectId"/> is null, otherwise for one project. Applying it
/// copies the items to a task, so later edits here never change existing tasks (FR-CHK-02).
/// </summary>
public sealed class ChecklistTemplate
{
    public const int NameMaxLength = 200;
    public const int ItemTextMaxLength = 500;
    public const int MaxItems = 50;

    private readonly List<ChecklistTemplateItem> _items = [];

    private ChecklistTemplate()
    {
        Name = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid? ProjectId { get; private set; }

    public string Name { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid UpdatedBy { get; private set; }

    public uint Version { get; private set; }

    public IReadOnlyList<ChecklistTemplateItem> Items => _items;

    public static ChecklistTemplate Create(Guid? projectId, string name, IReadOnlyList<ChecklistItemDraft> items, Guid by, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var template = new ChecklistTemplate { Id = Guid.CreateVersion7(now), ProjectId = projectId, IsActive = true, CreatedAt = now, CreatedBy = by };
        template.Update(name, true, items, by, now);
        return template;
    }

    /// <summary>Renames, (de)activates and replaces the items in the given order.</summary>
    public void Update(string name, bool isActive, IReadOnlyList<ChecklistItemDraft> items, Guid by, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(items);
        Name = name.Trim();
        IsActive = isActive;
        _items.Clear();
        for (var i = 0; i < items.Count; i++)
        {
            _items.Add(ChecklistTemplateItem.Create(Id, i + 1, items[i], now));
        }

        UpdatedAt = now;
        UpdatedBy = by;
    }
}

public sealed class ChecklistTemplateItem
{
    private ChecklistTemplateItem()
    {
        Text = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid TemplateId { get; private set; }

    public int SortOrder { get; private set; }

    public string Text { get; private set; }

    public bool IsMandatory { get; private set; }

    internal static ChecklistTemplateItem Create(Guid templateId, int sortOrder, ChecklistItemDraft draft, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Text);
        return new ChecklistTemplateItem
        {
            Id = Guid.CreateVersion7(now),
            TemplateId = templateId,
            SortOrder = sortOrder,
            Text = draft.Text.Trim(),
            IsMandatory = draft.IsMandatory,
        };
    }
}

/// <summary>
/// A checklist line on one task: copied from a template or added for this task only (FR-CHK-03). The assignee ticks it,
/// optionally with a note; who and when are stored (FR-CHK-04).
/// </summary>
public sealed class TaskChecklistItem
{
    public const int NoteMaxLength = 1_000;

    private TaskChecklistItem()
    {
        Text = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid TaskId { get; private set; }

    /// <summary>Null for task-specific items.</summary>
    public Guid? SourceTemplateItemId { get; private set; }

    public int SortOrder { get; private set; }

    public string Text { get; private set; }

    public bool IsMandatory { get; private set; }

    public bool IsCompleted { get; private set; }

    public Guid? CompletedBy { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Set when a TL reopens the item while requesting a revision (FR-CHK-06, Phase 8).</summary>
    public int? ReopenedInRevision { get; private set; }

    public uint Version { get; private set; }

    public static TaskChecklistItem FromTemplate(Guid taskId, ChecklistTemplateItem source, int sortOrder, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new TaskChecklistItem
        {
            Id = Guid.CreateVersion7(now),
            TaskId = taskId,
            SourceTemplateItemId = source.Id,
            SortOrder = sortOrder,
            Text = source.Text,
            IsMandatory = source.IsMandatory,
        };
    }

    public static TaskChecklistItem TaskSpecific(Guid taskId, ChecklistItemDraft draft, int sortOrder, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Text);
        return new TaskChecklistItem
        {
            Id = Guid.CreateVersion7(now),
            TaskId = taskId,
            SortOrder = sortOrder,
            Text = draft.Text.Trim(),
            IsMandatory = draft.IsMandatory,
        };
    }

    public Result Complete(Guid by, string? note, DateTimeOffset now)
    {
        if (IsCompleted)
        {
            return Error.Conflict("checklist.already_completed", "This item is already ticked.");
        }

        IsCompleted = true;
        CompletedBy = by;
        CompletedAt = now;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        return Result.Success();
    }

    public Result Uncomplete()
    {
        if (!IsCompleted)
        {
            return Error.Conflict("checklist.not_completed", "This item is not ticked.");
        }

        IsCompleted = false;
        CompletedBy = null;
        CompletedAt = null;
        Note = null;
        return Result.Success();
    }
}
