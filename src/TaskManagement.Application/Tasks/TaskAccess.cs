using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Access;
using TaskManagement.Application.Common;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Projects;
using TaskManagement.Domain.Tasks;

namespace TaskManagement.Application.Tasks;

/// <summary>A task the current member (<see cref="UserId"/>) may see, with what they may do in its project.</summary>
public sealed record VisibleTask(TaskItem Task, Project Project, ProjectPermissions Permissions, bool IsOwn, Guid UserId)
{
    /// <summary>The member is the task's current assignee (work, algorithm and checklist actions are theirs).</summary>
    public bool IsAssignee => Task.AssigneeId == UserId;

    /// <summary>
    /// An Own-scoped grant applies only to the member's own tasks; a Project or Global grant applies to every task here.
    /// </summary>
    public bool Allows(string code) =>
        Permissions.Scope(code) switch
        {
            null => false,
            PermissionScope.Own => IsOwn,
            _ => true,
        };
}

/// <summary>
/// Task visibility (FR-TASK-06, FR-ROLE-06): tasks.view with Project or Global scope shows every task in the project;
/// with Own scope only tasks the member is or was assigned to. Anything else is "not found" (edge case 6).
/// </summary>
public sealed class TaskAccess(IAppDbContext db, ICurrentUser currentUser, ProjectAccess projects)
{
    public Guid CurrentUserId => currentUser.RequiredUserId;

    public async Task<VisibleTask?> LoadAsync(Guid taskId, bool track, CancellationToken cancellationToken)
    {
        var query = track ? db.Tasks : db.Tasks.AsNoTracking();
        var task = await query.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
        {
            return null;
        }

        var permissions = await projects.ForProjectAsync(task.ProjectId, cancellationToken);
        var scope = permissions.Scope(PermissionCatalog.TasksView);
        if (scope is null)
        {
            return null;
        }

        var userId = currentUser.RequiredUserId;
        var isOwn = task.AssigneeId == userId
            || await db.TaskAssignments.AnyAsync(a => a.TaskId == task.Id && a.ToUserId == userId, cancellationToken);
        if (scope == PermissionScope.Own && !isOwn)
        {
            return null;
        }

        var project = await (track ? db.Projects : db.Projects.AsNoTracking()).FirstAsync(p => p.Id == task.ProjectId, cancellationToken);
        return new VisibleTask(task, project, permissions, isOwn, userId);
    }

    /// <summary>Tasks the current member may see, for list and search queries.</summary>
    public async Task<IQueryable<TaskItem>> VisibleTasksAsync(CancellationToken cancellationToken)
    {
        var scope = await projects.ProjectsWithAsync(PermissionCatalog.TasksView, cancellationToken);
        var tasks = db.Tasks.AsNoTracking();
        if (scope.Everywhere)
        {
            return tasks;
        }

        var userId = currentUser.RequiredUserId;
        var projectWide = scope.ProjectWide;
        var ownOnly = scope.OwnOnly;
        return tasks.Where(t => projectWide.Contains(t.ProjectId)
            || (ownOnly.Contains(t.ProjectId)
                && (t.AssigneeId == userId || db.TaskAssignments.Any(a => a.TaskId == t.Id && a.ToUserId == userId))));
    }

    /// <summary>Comments need tasks.comment: project-wide, or Own scope on a task the member is or was assigned to.</summary>
    public static bool CanComment(VisibleTask visible)
    {
        ArgumentNullException.ThrowIfNull(visible);
        return visible.Allows(PermissionCatalog.TasksComment);
    }
}
