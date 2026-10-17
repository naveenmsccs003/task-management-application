using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Access;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Checklists;
using TaskManagement.Application.Common;
using TaskManagement.Application.Notifications;
using TaskManagement.Application.Work;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Common;
using TaskManagement.Domain.Notifications;
using TaskManagement.Domain.Projects;
using TaskManagement.Domain.Tasks;
using TaskManagement.Domain.Time;

namespace TaskManagement.Application.Tasks;

/// <summary>
/// Task changes for Phase 5: create (T1), edit (T16), first assignment (T2), cancel (T15) and deleting an untouched draft.
/// Order of checks (docs/api-design.md §2): not visible → 404, no permission → 403, workflow or project rule → 409.
/// </summary>
public sealed class TaskCommands(
    IAppDbContext db,
    ICurrentUser currentUser,
    ProjectAccess projects,
    TaskAccess access,
    TaskQueries queries,
    PermissionService permissions,
    ChecklistTemplates templates,
    TaskChecklist checklist,
    WorkService work,
    Notifier notifier,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public async Task<Result<TaskDto>> CreateAsync(CreateTaskCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.ProjectId is not { } projectId)
        {
            return new FieldErrors().Add("projectId", "Every task must belong to a project.").ToError();
        }

        var loaded = await projects.LoadVisibleAsync(projectId, track: false, cancellationToken);
        if (loaded is not { } found)
        {
            return CommonErrors.NotFound("Project");
        }

        if (!found.Permissions.HasProjectWide(PermissionCatalog.TasksCreate))
        {
            return CommonErrors.Forbidden("create tasks in this project");
        }

        var errors = Validate(command.Title, command.Description, command.StartDate, command.DueDate, command.AllocatedMinutes, command.Labels);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var project = found.Project;
        if (!project.AcceptsNewTasks)
        {
            return Error.Conflict("project.not_active", $"Tasks can only be created in active projects. This project is {Project.Describe(project.Status)}.");
        }

        if (!await HasActiveLeadAsync(projectId, cancellationToken))
        {
            return Error.Conflict("project.no_lead", "Add an active Team Leader or Project Manager to the project before creating tasks.");
        }

        // An explicit template must be usable here; otherwise the project's default applies if it is still active.
        Domain.Checklists.ChecklistTemplate? template = null;
        if (command.ChecklistTemplateId is { } templateId)
        {
            template = await templates.FindUsableAsync(templateId, projectId, cancellationToken);
            if (template is null)
            {
                return new FieldErrors().Add("checklistTemplateId", "Choose an active global template or one of this project's templates.").ToError();
            }
        }
        else if (project.DefaultChecklistTemplateId is { } defaultId)
        {
            template = await templates.FindUsableAsync(defaultId, projectId, cancellationToken);
        }

        var details = new TaskDetails(
            command.Title!,
            command.Description,
            command.Type ?? TaskType.Feature,
            command.Priority ?? TaskPriority.Medium,
            command.StartDate,
            command.DueDate,
            NormalizeAllocation(command.AllocatedMinutes),
            command.AlgorithmRequired ?? project.AlgorithmRequiredDefault,
            TaskItem.NormalizeLabels(command.Labels));

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var number = await db.AllocateTaskNumberAsync(projectId, cancellationToken);
        var task = TaskItem.Create(projectId, project.Key, number, details, currentUser.RequiredUserId, timeProvider.GetUtcNow());
        db.Tasks.Add(task);
        if (template is not null)
        {
            checklist.Apply(task, template);
        }

        auditLog.Record("task.created", "Task", task.Id.ToString(), new AuditChanges()
            .Set("key", task.Key)
            .Set("title", task.Title)
            .Set("type", task.Type)
            .Set("priority", task.Priority)
            .Set("startDate", task.StartDate)
            .Set("dueDate", task.DueDate)
            .Set("allocatedMinutes", task.AllocatedMinutes)
            .Set("algorithmRequired", task.AlgorithmRequired)
            .Set("labels", task.Labels)
            .Set("checklistTemplateId", task.ChecklistTemplateId), projectId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await queries.GetAsync(task.Id, cancellationToken);
    }

    public async Task<Result<TaskDto>> UpdateAsync(Guid id, UpdateTaskCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var visible = await access.LoadAsync(id, track: true, cancellationToken);
        if (visible is null)
        {
            return CommonErrors.NotFound("Task");
        }

        if (!visible.Permissions.HasProjectWide(PermissionCatalog.TasksUpdate))
        {
            return CommonErrors.Forbidden("edit this task");
        }

        if (visible.Project.IsReadOnly)
        {
            return Project.ReadOnlyError();
        }

        var errors = Validate(command.Title, command.Description, command.StartDate, command.DueDate, command.AllocatedMinutes, command.Labels);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var task = visible.Task;
        db.ExpectVersion(task, command.Version);
        var details = new TaskDetails(
            command.Title!,
            command.Description,
            command.Type ?? task.Type,
            command.Priority ?? task.Priority,
            command.StartDate,
            command.DueDate,
            NormalizeAllocation(command.AllocatedMinutes),
            command.AlgorithmRequired ?? task.AlgorithmRequired,
            TaskItem.NormalizeLabels(command.Labels));
        var changes = new AuditChanges()
            .Change("title", task.Title, details.Title.Trim())
            .Change("description", task.Description, details.Description?.Trim() ?? string.Empty)
            .Change("type", task.Type, details.Type)
            .Change("priority", task.Priority, details.Priority)
            .Change("startDate", task.StartDate, details.StartDate)
            .Change("dueDate", task.DueDate, details.DueDate)
            .Change("allocatedMinutes", task.AllocatedMinutes, details.AllocatedMinutes)
            .Change("algorithmRequired", task.AlgorithmRequired, details.AlgorithmRequired)
            .Change("labels", string.Join(", ", task.Labels), string.Join(", ", details.Labels));
        var previousStatus = task.Status;

        var result = task.UpdateDetails(details, currentUser.RequiredUserId, timeProvider.GetUtcNow());
        if (result.IsFailure)
        {
            return result.Error!;
        }

        changes.Change("status", previousStatus, task.Status);
        if (!changes.IsEmpty)
        {
            auditLog.Record("task.updated", "Task", task.Id.ToString(), changes, task.ProjectId);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await queries.GetAsync(task.Id, cancellationToken);
    }

    /// <summary>T2. The assignee must be an active project member whose project role can work on tasks (FR-ASG-01).</summary>
    public async Task<Result<TaskDto>> AssignAsync(Guid id, AssignTaskCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var visible = await access.LoadAsync(id, track: true, cancellationToken);
        if (visible is null)
        {
            return CommonErrors.NotFound("Task");
        }

        if (!visible.Permissions.HasProjectWide(PermissionCatalog.TasksAssign))
        {
            return CommonErrors.Forbidden("assign this task");
        }

        if (visible.Project.IsReadOnly)
        {
            return Project.ReadOnlyError();
        }

        var task = visible.Task;
        if (!await CanWorkInProjectAsync(task.ProjectId, command.AssigneeId, cancellationToken))
        {
            return new FieldErrors().Add("assigneeId", "Choose an active project member whose project role can work on tasks.").ToError();
        }

        var result = task.Assign(command.AssigneeId, command.DueDate, currentUser.RequiredUserId, timeProvider.GetUtcNow());
        if (result.IsFailure)
        {
            return result.Error!;
        }

        // FR-ASG-06: the assignee and the project's Team Leaders hear about it.
        var assigneeName = await db.Users.Where(u => u.Id == command.AssigneeId).Select(u => u.FullName).FirstAsync(cancellationToken);
        var link = $"/tasks/{task.Id}";
        await notifier.NotifyAsync(
            [command.AssigneeId], NotificationType.TaskAssigned, $"{task.Key} was assigned to you",
            $"“{task.Title}” in {visible.Project.Name} is now yours. Due {task.DueDate:yyyy-MM-dd}.", link, task.ProjectId, task.Id, cancellationToken);
        var leads = (await notifier.ProjectLeadsAsync(task.ProjectId, cancellationToken)).Where(id => id != command.AssigneeId);
        await notifier.NotifyAsync(
            leads, NotificationType.TaskAssigned, $"{task.Key} was assigned to {assigneeName}",
            $"“{task.Title}” in {visible.Project.Name} was assigned to {assigneeName}.", link, task.ProjectId, task.Id, cancellationToken);

        auditLog.Record("task.assigned", "Task", task.Id.ToString(), new AuditChanges()
            .Set("assigneeId", command.AssigneeId)
            .Set("dueDate", task.DueDate)
            .Set("status", task.Status), task.ProjectId);
        await db.SaveChangesAsync(cancellationToken);
        return await queries.GetAsync(task.Id, cancellationToken);
    }

    public async Task<Result<TaskDto>> CancelAsync(Guid id, CancelTaskCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var visible = await access.LoadAsync(id, track: true, cancellationToken);
        if (visible is null)
        {
            return CommonErrors.NotFound("Task");
        }

        if (!visible.Permissions.HasProjectWide(PermissionCatalog.TasksCancel))
        {
            return CommonErrors.Forbidden("cancel this task");
        }

        if (visible.Project.IsReadOnly)
        {
            return Project.ReadOnlyError();
        }

        var errors = new FieldErrors()
            .Required("reason", command.Reason, "A reason")
            .MaxLength("reason", command.Reason?.Trim(), TaskItem.ReasonMaxLength, "The reason");
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var task = visible.Task;
        var previousStatus = task.Status;
        var now = timeProvider.GetUtcNow();
        var result = task.Cancel(command.Reason!, currentUser.RequiredUserId, now);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        // T15: cancelling an In progress task ends its work session.
        await work.CloseActiveSessionAsync(task.Id, SessionEndReason.Cancelled, now, cancellationToken);

        auditLog.Record("task.cancelled", "Task", task.Id.ToString(), new AuditChanges()
            .Change("status", previousStatus, task.Status)
            .Set("reason", task.CancelReason), task.ProjectId);
        await db.SaveChangesAsync(cancellationToken);
        return await queries.GetAsync(task.Id, cancellationToken);
    }

    /// <summary>
    /// Draft tasks with no history may be deleted (FR-TASK-09). A Draft has never been assigned, so "no history" means no
    /// comments; anything else must be cancelled instead.
    /// </summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var visible = await access.LoadAsync(id, track: true, cancellationToken);
        if (visible is null)
        {
            return CommonErrors.NotFound("Task");
        }

        if (!visible.Permissions.HasProjectWide(PermissionCatalog.TasksDeleteDraft))
        {
            return CommonErrors.Forbidden("delete this task");
        }

        if (visible.Project.IsReadOnly)
        {
            return Project.ReadOnlyError();
        }

        var task = visible.Task;
        if (task.Status != TaskItemStatus.Draft
            || await db.TaskComments.AnyAsync(c => c.TaskId == id, cancellationToken)
            || await db.TaskAssignments.AnyAsync(a => a.TaskId == id, cancellationToken))
        {
            return Error.Conflict("task.has_history", $"Only draft tasks without comments can be deleted. Cancel {task.Key} with a reason instead.");
        }

        var history = await db.TaskStatusHistory.Where(h => h.TaskId == id).ToListAsync(cancellationToken);
        db.TaskStatusHistory.RemoveRange(history);
        db.Tasks.Remove(task);
        auditLog.Record("task.deleted", "Task", task.Id.ToString(), new AuditChanges()
            .Set("key", task.Key)
            .Set("title", task.Title), task.ProjectId);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static FieldErrors Validate(
        string? title, string? description, DateOnly? startDate, DateOnly? dueDate, int? allocatedMinutes, IReadOnlyList<string?>? labels)
    {
        var errors = new FieldErrors()
            .Required("title", title, "Title")
            .MaxLength("title", title?.Trim(), TaskItem.TitleMaxLength, "Title")
            .MaxLength("description", description, TaskItem.DescriptionMaxLength, "Description");
        if (startDate is { } start && dueDate is { } due && due < start)
        {
            errors.Add("dueDate", "The due date must be on or after the start date.");
        }

        if (allocatedMinutes is < 0 or > TaskItem.AllocatedMinutesMax)
        {
            errors.Add("allocatedMinutes", $"Allocated time must be between 0 and {TaskItem.AllocatedMinutesMax / 60} hours.");
        }

        var normalized = TaskItem.NormalizeLabels(labels);
        if (normalized.Count > TaskItem.MaxLabels)
        {
            errors.Add("labels", $"Use at most {TaskItem.MaxLabels} labels.");
        }

        if (normalized.Exists(l => l.Length > TaskItem.LabelMaxLength))
        {
            errors.Add("labels", $"Each label must be at most {TaskItem.LabelMaxLength} characters.");
        }

        return errors;
    }

    /// <summary>0 means "not estimated", like an empty value (edge case 16).</summary>
    private static int? NormalizeAllocation(int? minutes) => minutes is null or 0 ? null : minutes;

    private Task<bool> HasActiveLeadAsync(Guid projectId, CancellationToken cancellationToken) =>
        db.ProjectMembers.AnyAsync(
            m => m.ProjectId == projectId
                && m.RemovedAt == null
                && (m.ProjectRoleId == SystemRoles.TeamLeaderId || m.ProjectRoleId == SystemRoles.ProjectManagerId)
                && db.Users.Any(u => u.Id == m.UserId && u.IsActive),
            cancellationToken);

    private async Task<bool> CanWorkInProjectAsync(Guid projectId, Guid userId, CancellationToken cancellationToken)
    {
        var roleId = await db.ProjectMembers
            .Where(m => m.ProjectId == projectId && m.UserId == userId && m.RemovedAt == null && db.Users.Any(u => u.Id == userId && u.IsActive))
            .Select(m => (Guid?)m.ProjectRoleId)
            .FirstOrDefaultAsync(cancellationToken);
        return roleId is { } id && (await permissions.GetRoleGrantsAsync(id, cancellationToken)).ContainsKey(PermissionCatalog.TasksWork);
    }
}
