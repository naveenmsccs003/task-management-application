using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Checklists;
using TaskManagement.Application.Common;
using TaskManagement.Application.Time;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Common;
using TaskManagement.Domain.Projects;
using TaskManagement.Domain.Tasks;

namespace TaskManagement.Application.Tasks;

/// <summary>Task reads: the filtered list (FR-TASK-07), one task with its allowed actions, and its timeline.</summary>
public sealed class TaskQueries(IAppDbContext db, TaskAccess access, TimeService time)
{
    /// <summary>Fields the list can be sorted by (docs/api-design.md §1: only allow-listed fields).</summary>
    public static IReadOnlyList<string> SortFields { get; } = ["key", "title", "dueDate", "priority", "createdAt", "updatedAt"];

    public async Task<Result<PagedResult<TaskListItemDto>>> ListAsync(TaskListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var sort = ParseSort(query.Sort);
        if (sort is null)
        {
            return new FieldErrors().Add("sort", $"Sort by one or more of: {string.Join(", ", SortFields)} (prefix with - for descending).").ToError();
        }

        var tasks = await access.VisibleTasksAsync(cancellationToken);
        if (query.ProjectId is { } projectId)
        {
            tasks = tasks.Where(t => t.ProjectId == projectId);
        }

        if (query.Statuses is { Count: > 0 } statuses)
        {
            tasks = tasks.Where(t => statuses.Contains(t.Status));
        }

        if (query.AssigneeId is { } assigneeId)
        {
            tasks = tasks.Where(t => t.AssigneeId == assigneeId);
        }

        if (query.Priorities is { Count: > 0 } priorities)
        {
            tasks = tasks.Where(t => priorities.Contains(t.Priority));
        }

        if (query.DueFrom is { } dueFrom)
        {
            tasks = tasks.Where(t => t.DueDate >= dueFrom);
        }

        if (query.DueTo is { } dueTo)
        {
            tasks = tasks.Where(t => t.DueDate <= dueTo);
        }

        // The flag is kept by the due-date job in each assignee's time zone (FR-DL-05).
        if (query.Overdue is { } overdue)
        {
            tasks = tasks.Where(t => t.IsOverdue == overdue);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var text = query.Q.Trim();
            if (LooksLikeKey(text))
            {
                var key = text.ToUpperInvariant();
                tasks = tasks.Where(t => t.Key == key);
            }
            else
            {
                var lowered = text.ToLowerInvariant();
                tasks = tasks.Where(t => t.Title.ToLower().Contains(lowered));
            }
        }

        var total = await tasks.CountAsync(cancellationToken);
        var rows = await ApplySort(tasks, sort)
            .Skip(query.Page.Skip)
            .Take(query.Page.SafePageSize)
            .Select(t => new
            {
                t.Id,
                t.Key,
                t.ProjectId,
                ProjectKey = db.Projects.Where(p => p.Id == t.ProjectId).Select(p => p.Key).First(),
                t.Title,
                t.Type,
                t.Priority,
                t.Status,
                Assignee = db.Users.Where(u => u.Id == t.AssigneeId).Select(u => new UserRef(u.Id, u.FullName)).FirstOrDefault(),
                t.DueDate,
                t.IsOverdue,
                t.Labels,
                t.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new TaskListItemDto(
                r.Id, r.Key, r.ProjectId, r.ProjectKey, r.Title, r.Type, r.Priority, r.Status, r.Assignee, r.DueDate,
                r.IsOverdue, r.Labels, r.UpdatedAt))
            .ToList();
        return new PagedResult<TaskListItemDto>(items, query.Page.SafePage, query.Page.SafePageSize, total);
    }

    public async Task<Result<TaskDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var visible = await access.LoadAsync(id, track: false, cancellationToken);
        return visible is null ? CommonErrors.NotFound("Task") : await ToDtoAsync(visible, cancellationToken);
    }

    /// <summary>Task keys (e.g. <c>TMA-42</c>) are accepted only here and in search (docs/api-design.md §1).</summary>
    public async Task<Result<TaskDto>> GetByKeyAsync(string key, CancellationToken cancellationToken)
    {
        var normalized = key?.Trim().ToUpperInvariant() ?? string.Empty;
        var id = await db.Tasks.AsNoTracking().Where(t => t.Key == normalized).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(cancellationToken);
        return id is { } found ? await GetAsync(found, cancellationToken) : CommonErrors.NotFound("Task");
    }

    /// <summary>The task's state changes and assignments, oldest first.</summary>
    public async Task<Result<IReadOnlyList<TaskHistoryEntryDto>>> HistoryAsync(Guid id, CancellationToken cancellationToken)
    {
        var visible = await access.LoadAsync(id, track: false, cancellationToken);
        if (visible is null)
        {
            return CommonErrors.NotFound("Task");
        }

        var statusChanges = await db.TaskStatusHistory.AsNoTracking().Where(h => h.TaskId == id).ToListAsync(cancellationToken);
        var assignments = await db.TaskAssignments.AsNoTracking().Where(a => a.TaskId == id).ToListAsync(cancellationToken);
        var userIds = statusChanges.Where(h => h.ActorId != null).Select(h => h.ActorId!.Value)
            .Concat(assignments.SelectMany(a => new[] { a.AssignedBy, a.ToUserId }))
            .Concat(assignments.Where(a => a.FromUserId is not null).Select(a => a.FromUserId!.Value))
            .Distinct()
            .ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new UserRef(u.Id, u.FullName), cancellationToken);
        UserRef? Name(Guid? userId) => userId is { } value && names.TryGetValue(value, out var user) ? user : null;

        IReadOnlyList<TaskHistoryEntryDto> entries = statusChanges
            .Select(h => new TaskHistoryEntryDto(
                h.OccurredAt, "status", Name(h.ActorId), h.Revision, TaskWorkflow.ToActionName(h.Action), h.FromStatus, h.ToStatus, null, null, h.Comment))
            .Concat(assignments.Select(a => new TaskHistoryEntryDto(
                a.OccurredAt, "assignment", Name(a.AssignedBy), null, a.Kind == AssignmentKind.Initial ? "assign" : "reassign", null, null,
                Name(a.FromUserId), Name(a.ToUserId), a.Reason)))
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Kind == "assignment" ? 0 : 1)
            .ToList();
        return Result.Success(entries);
    }

    internal async Task<TaskDto> ToDtoAsync(VisibleTask visible, CancellationToken cancellationToken)
    {
        var task = visible.Task;
        var userIds = new[] { task.CreatedBy, task.AssigneeId ?? Guid.Empty };
        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new UserRef(u.Id, u.FullName), cancellationToken);
        var hasComments = await db.TaskComments.AnyAsync(c => c.TaskId == task.Id, cancellationToken);
        var spent = await time.SpentSecondsAsync(task.Id, cancellationToken);
        var pending = await db.ApprovalRequests.AsNoTracking()
            .Where(r => r.TaskId == task.Id && r.Status == ApprovalRequestStatus.Pending)
            .Select(r => r.Type)
            .ToListAsync(cancellationToken);
        var checklist = await db.TaskChecklistItems.AsNoTracking()
            .Where(i => i.TaskId == task.Id)
            .GroupBy(i => 1)
            .Select(g => new ChecklistSummaryDto(
                g.Count(), g.Count(i => i.IsCompleted), g.Count(i => i.IsMandatory), g.Count(i => i.IsMandatory && i.IsCompleted)))
            .FirstOrDefaultAsync(cancellationToken) ?? new ChecklistSummaryDto(0, 0, 0, 0);

        return new TaskDto(
            task.Id,
            task.Key,
            task.ProjectId,
            visible.Project.Key,
            visible.Project.Name,
            visible.Project.IsReadOnly,
            task.Title,
            task.Description,
            task.Type,
            task.Priority,
            task.Status,
            task.AssigneeId is { } assigneeId ? names.GetValueOrDefault(assigneeId) : null,
            task.StartDate,
            task.DueDate,
            task.OriginalDueDate,
            task.AllocatedMinutes,
            spent,
            task.AlgorithmRequired,
            task.AlgorithmApprovedSubmissionId is not null,
            task.Labels,
            task.CurrentRevision,
            task.ApprovedRevision,
            new TaskFlagsDto(
                task.IsOverdue,
                task.AllocatedMinutes is { } minutes && spent > minutes * 60L,
                pending.Contains(ApprovalRequestType.DeadlineExtension),
                pending.Contains(ApprovalRequestType.Reassignment)),
            checklist,
            task.CancelReason,
            names.GetValueOrDefault(task.CreatedBy) ?? new UserRef(task.CreatedBy, "Unknown"),
            task.CreatedAt,
            task.UpdatedAt,
            AllowedActions(visible, hasComments, pending),
            visible.Allows(PermissionCatalog.AlgorithmsView),
            visible.Allows(PermissionCatalog.TimeView),
            visible.Allows(PermissionCatalog.DelaysView),
            task.Version);
    }

    /// <summary>
    /// The actions offered to the caller, using the same checks the commands enforce. Workflow actions of later phases
    /// (submit for review, review, close, …) are added here as they are implemented.
    /// </summary>
    internal static IReadOnlyList<string> AllowedActions(VisibleTask visible, bool hasComments, IReadOnlyCollection<ApprovalRequestType> pendingRequests)
    {
        var (task, project, permissions, _, _) = visible;
        if (project.IsReadOnly)
        {
            return [];
        }

        if (TaskWorkflow.IsFinal(task.Status))
        {
            return task.Status == TaskItemStatus.Closed && permissions.HasProjectWide(PermissionCatalog.TasksReopen) ? ["reopen"] : [];
        }

        var actions = new List<string>();
        if (permissions.HasProjectWide(PermissionCatalog.TasksUpdate))
        {
            actions.Add("update");
        }

        if (permissions.HasProjectWide(PermissionCatalog.TasksAssign) && TaskWorkflow.IsAllowed(task.Status, TaskAction.Assign))
        {
            actions.Add("assign");
        }

        if (permissions.HasProjectWide(PermissionCatalog.TasksCancel) && TaskWorkflow.IsAllowed(task.Status, TaskAction.Cancel))
        {
            actions.Add("cancel");
        }

        if (permissions.HasProjectWide(PermissionCatalog.TasksDeleteDraft) && task.Status == TaskItemStatus.Draft && !hasComments)
        {
            actions.Add("delete");
        }

        if (TaskAccess.CanComment(visible))
        {
            actions.Add("comment");
        }

        // The assignee's own actions (FR-WORK-01, FR-ALG-01, FR-CHK-04).
        if (visible.IsAssignee)
        {
            if (visible.Allows(PermissionCatalog.AlgorithmsSubmit) && TaskWorkflow.IsAllowed(task.Status, TaskAction.SubmitAlgorithm))
            {
                actions.Add("submitAlgorithm");
            }

            if (visible.Allows(PermissionCatalog.TasksWork))
            {
                foreach (var (action, name) in new[] { (TaskAction.Start, "start"), (TaskAction.Pause, "pause"), (TaskAction.Resume, "resume"), (TaskAction.Stop, "stop") })
                {
                    if (TaskWorkflow.IsAllowed(task.Status, action))
                    {
                        actions.Add(name);
                    }
                }
            }

            if (visible.Allows(PermissionCatalog.ChecklistsComplete) && TaskChecklist.CanTick(task.Status))
            {
                actions.Add("completeChecklist");
            }
        }

        // Separation of duties (FR-ALG-05): an assignee never reviews their own algorithm.
        if (!visible.IsAssignee && permissions.HasProjectWide(PermissionCatalog.AlgorithmsApprove) && task.Status == TaskItemStatus.AlgorithmSubmitted)
        {
            actions.Add("reviewAlgorithm");
        }

        if (permissions.HasProjectWide(PermissionCatalog.ChecklistsManageProject) && TaskChecklist.CanAddItems(task.Status))
        {
            actions.Add("manageChecklist");
        }

        if (permissions.HasProjectWide(PermissionCatalog.TimeAdjust))
        {
            actions.Add("adjustTime");
        }

        // Review and closure (T10–T13).
        if (visible.IsAssignee && visible.Allows(PermissionCatalog.TasksSubmit) && TaskWorkflow.IsAllowed(task.Status, TaskAction.SubmitReview))
        {
            actions.Add("submitReview");
        }

        if (!visible.IsAssignee && permissions.HasProjectWide(PermissionCatalog.TasksReview) && task.Status == TaskItemStatus.InReview)
        {
            actions.Add("review");
        }

        if (permissions.HasProjectWide(PermissionCatalog.TasksClose) && task.Status == TaskItemStatus.Approved && task.ApprovedRevision == task.CurrentRevision)
        {
            actions.Add("close");
        }

        // Requests and delays (task-workflow.md §5–7).
        if (permissions.HasProjectWide(PermissionCatalog.TasksAssign) && TaskWorkflow.CanReassign(task.Status)
            && !pendingRequests.Contains(ApprovalRequestType.Reassignment))
        {
            actions.Add("requestReassignment");
        }

        var extensionScope = permissions.Scope(PermissionCatalog.DeadlinesRequest);
        if (task.DueDate is not null && task.Status != TaskItemStatus.Draft && TaskWorkflow.IsOpen(task.Status)
            && !pendingRequests.Contains(ApprovalRequestType.DeadlineExtension)
            && (extensionScope >= PermissionScope.Project || (extensionScope == PermissionScope.Own && visible.IsAssignee)))
        {
            actions.Add("requestExtension");
        }

        if (visible.IsAssignee && visible.Allows(PermissionCatalog.DelaysReport) && TaskWorkflow.IsOpen(task.Status))
        {
            actions.Add("reportDelay");
        }

        return actions;
    }

    private static bool LooksLikeKey(string text)
    {
        var dash = text.LastIndexOf('-');
        return dash > 0
            && dash < text.Length - 1
            && Project.IsValidKey(text[..dash].ToUpperInvariant())
            && text[(dash + 1)..].All(char.IsAsciiDigit);
    }

    private static List<(string Field, bool Descending)>? ParseSort(string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
        {
            return [("dueDate", false), ("key", false)];
        }

        var result = new List<(string, bool)>();
        foreach (var part in sort.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var descending = part.StartsWith('-');
            var name = descending ? part[1..] : part;
            var field = SortFields.FirstOrDefault(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));
            if (field is null)
            {
                return null;
            }

            result.Add((field, descending));
        }

        return result.Count == 0 ? null : result;
    }

    private IOrderedQueryable<TaskItem> ApplySort(IQueryable<TaskItem> tasks, List<(string Field, bool Descending)> sort)
    {
        IOrderedQueryable<TaskItem>? ordered = null;
        foreach (var (field, descending) in sort)
        {
            ordered = field switch
            {
                "key" => Then(
                    Then(ordered, tasks, t => db.Projects.Where(p => p.Id == t.ProjectId).Select(p => p.Key).First(), descending),
                    tasks,
                    t => t.Number,
                    descending),
                "title" => Then(ordered, tasks, t => t.Title, descending),
                "dueDate" => Then(ordered, tasks, t => t.DueDate, descending),
                "priority" => Then(
                    ordered,
                    tasks,
                    t => t.Priority == TaskPriority.Critical ? 3 : t.Priority == TaskPriority.High ? 2 : t.Priority == TaskPriority.Medium ? 1 : 0,
                    descending),
                "createdAt" => Then(ordered, tasks, t => t.CreatedAt, descending),
                _ => Then(ordered, tasks, t => t.UpdatedAt, descending),
            };
        }

        // A unique final key keeps pages stable.
        return ordered!.ThenBy(t => t.Id);
    }

    private static IOrderedQueryable<TaskItem> Then<TKey>(
        IOrderedQueryable<TaskItem>? ordered, IQueryable<TaskItem> source, System.Linq.Expressions.Expression<Func<TaskItem, TKey>> key, bool descending) =>
        ordered is null
            ? descending ? source.OrderByDescending(key) : source.OrderBy(key)
            : descending ? ordered.ThenByDescending(key) : ordered.ThenBy(key);
}
