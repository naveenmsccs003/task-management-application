using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Access;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Common;
using TaskManagement.Domain.Projects;
using TaskManagement.Domain.Tasks;

namespace TaskManagement.Application.Projects;

/// <summary>Adding, re-roling and removing project members (FR-PROJ-02, FR-PROJ-05). Requires projects.members.manage in the project.</summary>
public sealed class ProjectMemberCommands(
    IAppDbContext db,
    ICurrentUser currentUser,
    ProjectAccess access,
    ProjectQueries queries,
    PermissionService permissions,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    private const int CandidateLimit = 20;

    private static readonly Error MembershipNotFound =
        Error.NotFound("project_member.not_found", "This person is not a member of the project.");

    private static readonly Error SelfChange =
        Error.Conflict("project.self_change", "You cannot add yourself, change your own project role or remove yourself. Ask another manager.");

    public async Task<Result<IReadOnlyList<MemberCandidateDto>>> CandidatesAsync(Guid projectId, string? search, CancellationToken cancellationToken)
    {
        var guard = await AuthorizeAsync(projectId, requireWritable: false, cancellationToken);
        if (guard.IsFailure)
        {
            return guard.Error!;
        }

        var users = db.Users.AsNoTracking().Where(u => u.IsActive
            && !db.ProjectMembers.Any(m => m.ProjectId == projectId && m.UserId == u.Id && m.RemovedAt == null));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var text = search.Trim().ToLowerInvariant();
            users = users.Where(u => u.FullName.ToLower().Contains(text) || u.Email.ToLower().Contains(text));
        }

        IReadOnlyList<MemberCandidateDto> candidates = await users
            .OrderBy(u => u.FullName)
            .Take(CandidateLimit)
            .Select(u => new MemberCandidateDto(u.Id, u.FullName, u.Email))
            .ToListAsync(cancellationToken);
        return Result.Success(candidates);
    }

    public async Task<Result<IReadOnlyList<ProjectMemberDto>>> AddAsync(Guid projectId, AddProjectMemberCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var guard = await AuthorizeAsync(projectId, requireWritable: true, cancellationToken);
        if (guard.IsFailure)
        {
            return guard.Error!;
        }

        var roleError = await ValidateRoleAsync(command.ProjectRoleId, cancellationToken);
        if (roleError is not null)
        {
            return roleError;
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return CommonErrors.NotFound("Member");
        }

        if (!user.IsActive)
        {
            return Error.Conflict("member.inactive", "Deactivated members cannot be added to projects.");
        }

        if (user.Id == currentUser.UserId)
        {
            return SelfChange;
        }

        if (await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == user.Id && m.RemovedAt == null, cancellationToken))
        {
            return Error.Conflict("project.already_member", $"{user.FullName} is already a member of this project.");
        }

        var membership = ProjectMember.Create(projectId, user.Id, command.ProjectRoleId, currentUser.RequiredUserId, timeProvider.GetUtcNow());
        db.ProjectMembers.Add(membership);
        auditLog.Record("project.member_added", "ProjectMember", membership.Id.ToString(), new AuditChanges()
            .Set("userId", user.Id)
            .Set("projectRoleId", command.ProjectRoleId), projectId);
        await db.SaveChangesAsync(cancellationToken);
        return await queries.MembersAsync(projectId, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<ProjectMemberDto>>> ChangeRoleAsync(
        Guid projectId, Guid userId, ChangeProjectMemberCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var guard = await AuthorizeAsync(projectId, requireWritable: true, cancellationToken);
        if (guard.IsFailure)
        {
            return guard.Error!;
        }

        var membership = await FindMembershipAsync(projectId, userId, cancellationToken);
        if (membership is null)
        {
            return MembershipNotFound;
        }

        if (userId == currentUser.UserId)
        {
            return SelfChange;
        }

        var roleError = await ValidateRoleAsync(command.ProjectRoleId, cancellationToken);
        if (roleError is not null)
        {
            return roleError;
        }

        if (membership.ProjectRoleId == command.ProjectRoleId)
        {
            return await queries.MembersAsync(projectId, cancellationToken);
        }

        // A member keeps their open tasks only if the new role can still work on tasks (FR-ASG-01).
        var newGrants = await permissions.GetRoleGrantsAsync(command.ProjectRoleId, cancellationToken);
        if (!newGrants.ContainsKey(PermissionCatalog.TasksWork))
        {
            var openTasks = await CountOpenTasksAsync(projectId, userId, cancellationToken);
            if (openTasks > 0)
            {
                return OpenTasksError(openTasks, "giving them a role that cannot work on tasks");
            }
        }

        var previous = membership.ProjectRoleId;
        membership.ChangeRole(command.ProjectRoleId);
        auditLog.Record("project.member_role_changed", "ProjectMember", membership.Id.ToString(), new AuditChanges()
            .Set("userId", userId)
            .Change("projectRoleId", previous, command.ProjectRoleId), projectId);
        await db.SaveChangesAsync(cancellationToken);
        return await queries.MembersAsync(projectId, cancellationToken);
    }

    /// <summary>Blocked while the member has open tasks in the project; reassign them first (FR-PROJ-05).</summary>
    public async Task<Result> RemoveAsync(Guid projectId, Guid userId, CancellationToken cancellationToken)
    {
        var guard = await AuthorizeAsync(projectId, requireWritable: true, cancellationToken);
        if (guard.IsFailure)
        {
            return guard.Error!;
        }

        var membership = await FindMembershipAsync(projectId, userId, cancellationToken);
        if (membership is null)
        {
            return MembershipNotFound;
        }

        if (userId == currentUser.UserId)
        {
            return SelfChange;
        }

        var openTasks = await CountOpenTasksAsync(projectId, userId, cancellationToken);
        if (openTasks > 0)
        {
            return OpenTasksError(openTasks, "removing them");
        }

        membership.Remove(currentUser.RequiredUserId, timeProvider.GetUtcNow());
        auditLog.Record("project.member_removed", "ProjectMember", membership.Id.ToString(), new AuditChanges()
            .Set("userId", userId)
            .Set("projectRoleId", membership.ProjectRoleId), projectId);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Result> AuthorizeAsync(Guid projectId, bool requireWritable, CancellationToken cancellationToken)
    {
        var loaded = await access.LoadVisibleAsync(projectId, track: false, cancellationToken);
        if (loaded is not { } found)
        {
            return CommonErrors.NotFound("Project");
        }

        if (!found.Permissions.HasProjectWide(PermissionCatalog.ProjectsMembersManage))
        {
            return CommonErrors.Forbidden("manage this project's members");
        }

        return requireWritable && found.Project.IsReadOnly ? Project.ReadOnlyError() : Result.Success();
    }

    /// <summary>Project roles can be any role except Admin, which is a system administration role, not a project role.</summary>
    private async Task<Error?> ValidateRoleAsync(Guid roleId, CancellationToken cancellationToken)
    {
        if (roleId == SystemRoles.AdminId || !await db.Roles.AnyAsync(r => r.Id == roleId, cancellationToken))
        {
            return new FieldErrors().Add("projectRoleId", "Choose a project role such as Team Leader, Project Manager, Developer or Viewer.").ToError();
        }

        return null;
    }

    private Task<ProjectMember?> FindMembershipAsync(Guid projectId, Guid userId, CancellationToken cancellationToken) =>
        db.ProjectMembers.FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId && m.RemovedAt == null, cancellationToken);

    private Task<int> CountOpenTasksAsync(Guid projectId, Guid userId, CancellationToken cancellationToken) =>
        db.Tasks.CountAsync(
            t => t.ProjectId == projectId && t.AssigneeId == userId
                && t.Status != TaskItemStatus.Approved && t.Status != TaskItemStatus.Closed && t.Status != TaskItemStatus.Cancelled,
            cancellationToken);

    private static Error OpenTasksError(int count, string action) =>
        Error.Conflict(
            "project.member_has_open_tasks",
            $"This member has {count} open task{(count == 1 ? string.Empty : "s")} in the project. Reassign {(count == 1 ? "it" : "them")} before {action}.");
}
