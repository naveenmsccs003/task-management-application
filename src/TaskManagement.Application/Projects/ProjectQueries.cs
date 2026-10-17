using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Access;
using TaskManagement.Application.Common;
using TaskManagement.Application.Identity;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Common;
using TaskManagement.Domain.Projects;
using TaskManagement.Domain.Tasks;

namespace TaskManagement.Application.Projects;

/// <summary>Project reads. Members see only their projects unless they hold a Global projects.view grant (FR-PROJ-03).</summary>
public sealed class ProjectQueries(IAppDbContext db, ICurrentUser currentUser, ProjectAccess access, PermissionService permissions)
{
    public async Task<PagedResult<ProjectListItemDto>> ListAsync(ProjectListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var userId = currentUser.RequiredUserId;
        var visible = await access.ProjectsWithAsync(PermissionCatalog.ProjectsView, cancellationToken);
        var projects = db.Projects.AsNoTracking();
        if (!visible.Everywhere)
        {
            var ids = visible.ProjectWide.Concat(visible.OwnOnly).ToList();
            projects = projects.Where(p => ids.Contains(p.Id));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            projects = projects.Where(p => p.Name.ToLower().Contains(search) || p.Key.ToLower().Contains(search));
        }

        if (query.Status is { } status)
        {
            projects = projects.Where(p => p.Status == status);
        }

        var total = await projects.CountAsync(cancellationToken);
        var items = await projects
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Skip(query.Page.Skip)
            .Take(query.Page.SafePageSize)
            .Select(p => new ProjectListItemDto(
                p.Id,
                p.Key,
                p.Name,
                p.Status,
                p.TargetEndDate,
                db.ProjectMembers.Count(m => m.ProjectId == p.Id && m.RemovedAt == null),
                db.Tasks.Count(t => t.ProjectId == p.Id
                    && t.Status != TaskItemStatus.Approved && t.Status != TaskItemStatus.Closed && t.Status != TaskItemStatus.Cancelled),
                db.ProjectMembers
                    .Where(m => m.ProjectId == p.Id && m.UserId == userId && m.RemovedAt == null)
                    .Join(db.Roles, m => m.ProjectRoleId, r => r.Id, (m, r) => new RoleRef(r.Id, r.Name))
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);
        return new PagedResult<ProjectListItemDto>(items, query.Page.SafePage, query.Page.SafePageSize, total);
    }

    /// <summary>Roles that can be given inside a project: every role except Admin (names only, for pickers).</summary>
    public async Task<IReadOnlyList<RoleRef>> ProjectRolesAsync(CancellationToken cancellationToken) =>
        await db.Roles.AsNoTracking()
            .Where(r => r.Id != SystemRoles.AdminId)
            .OrderBy(r => r.Name)
            .Select(r => new RoleRef(r.Id, r.Name))
            .ToListAsync(cancellationToken);

    public async Task<Result<ProjectDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var loaded = await access.LoadVisibleAsync(id, track: false, cancellationToken);
        if (loaded is not { } found)
        {
            return CommonErrors.NotFound("Project");
        }

        return await ToDtoAsync(found.Project, found.Permissions, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<PermissionGrantDto>>> MyPermissionsAsync(Guid id, CancellationToken cancellationToken)
    {
        var loaded = await access.LoadVisibleAsync(id, track: false, cancellationToken);
        if (loaded is not { } found)
        {
            return CommonErrors.NotFound("Project");
        }

        return Result.Success<IReadOnlyList<PermissionGrantDto>>(ToGrants(found.Permissions));
    }

    public async Task<Result<IReadOnlyList<ProjectMemberDto>>> MembersAsync(Guid id, CancellationToken cancellationToken)
    {
        var loaded = await access.LoadVisibleAsync(id, track: false, cancellationToken);
        if (loaded is null)
        {
            return CommonErrors.NotFound("Project");
        }

        var rows = await db.ProjectMembers
            .AsNoTracking()
            .Where(m => m.ProjectId == id && m.RemovedAt == null)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { Member = m, User = u })
            .Join(db.Roles, x => x.Member.ProjectRoleId, r => r.Id, (x, r) => new
            {
                x.User.Id,
                x.User.FullName,
                x.User.Email,
                x.User.IsActive,
                Role = new RoleRef(r.Id, r.Name),
                x.Member.AddedAt,
                OpenTasks = db.Tasks.Count(t => t.ProjectId == id && t.AssigneeId == x.User.Id
                    && t.Status != TaskItemStatus.Approved && t.Status != TaskItemStatus.Closed && t.Status != TaskItemStatus.Cancelled),
            })
            .OrderBy(x => x.FullName)
            .ToListAsync(cancellationToken);

        var result = new List<ProjectMemberDto>(rows.Count);
        foreach (var row in rows)
        {
            var grants = await permissions.GetRoleGrantsAsync(row.Role.Id, cancellationToken);
            result.Add(new ProjectMemberDto(
                row.Id, row.FullName, row.Email, row.IsActive, row.Role, grants.ContainsKey(PermissionCatalog.TasksWork), row.OpenTasks, row.AddedAt));
        }

        return result;
    }

    internal async Task<ProjectDto> ToDtoAsync(Project project, ProjectPermissions permissionsHere, CancellationToken cancellationToken)
    {
        var counts = await db.Projects
            .AsNoTracking()
            .Where(p => p.Id == project.Id)
            .Select(p => new
            {
                Members = db.ProjectMembers.Count(m => m.ProjectId == p.Id && m.RemovedAt == null),
                OpenTasks = db.Tasks.Count(t => t.ProjectId == p.Id
                    && t.Status != TaskItemStatus.Approved && t.Status != TaskItemStatus.Closed && t.Status != TaskItemStatus.Cancelled),
            })
            .FirstAsync(cancellationToken);
        var myRole = permissionsHere.ProjectRoleId is { } roleId
            ? await db.Roles.AsNoTracking().Where(r => r.Id == roleId).Select(r => new RoleRef(r.Id, r.Name)).FirstOrDefaultAsync(cancellationToken)
            : null;

        return new ProjectDto(
            project.Id,
            project.Key,
            project.Name,
            project.Description,
            project.Status,
            project.StartDate,
            project.TargetEndDate,
            project.AlgorithmRequiredDefault,
            project.CloseOnApproval,
            counts.Members,
            counts.OpenTasks,
            myRole,
            ToGrants(permissionsHere),
            project.CreatedAt,
            project.Version);
    }

    private static List<PermissionGrantDto> ToGrants(ProjectPermissions permissionsHere) =>
        permissionsHere.All()
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new PermissionGrantDto(g.Key, g.Value))
            .ToList();
}
