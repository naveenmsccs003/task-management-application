using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Common;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Projects;

namespace TaskManagement.Application.Access;

/// <summary>
/// What a member may do in one project (docs/permissions-matrix.md "How permissions work"): a Global grant on their
/// global role applies everywhere; otherwise their project role in this project decides, with its Project or Own scope.
/// Project and Own grants on the global role do not reach into projects the member does not belong to.
/// </summary>
public sealed class ProjectPermissions(
    Guid projectId,
    Guid? projectRoleId,
    EffectivePermissions global,
    IReadOnlyDictionary<string, PermissionScope> projectRoleGrants)
{
    public Guid ProjectId { get; } = projectId;

    /// <summary>The member's project role, or null when they are not a current member.</summary>
    public Guid? ProjectRoleId { get; } = projectRoleId;

    public bool IsMember => ProjectRoleId is not null;

    /// <summary>The widest scope the member holds for <paramref name="code"/> in this project, or null if not granted.</summary>
    public PermissionScope? Scope(string code)
    {
        PermissionScope? best = global.Has(code, PermissionScope.Global) ? PermissionScope.Global : null;
        if (IsMember && projectRoleGrants.TryGetValue(code, out var scope) && (best is null || scope > best))
        {
            best = scope;
        }

        return best;
    }

    /// <summary>Granted for the whole project (Project or Global scope).</summary>
    public bool HasProjectWide(string code) => Scope(code) >= PermissionScope.Project;

    /// <summary>Granted at any scope, including Own (the caller must still check ownership for Own).</summary>
    public bool HasAny(string code) => Scope(code) is not null;

    /// <summary>Every permission the member holds here, for the Angular UI (display only).</summary>
    public IReadOnlyDictionary<string, PermissionScope> All()
    {
        var all = new Dictionary<string, PermissionScope>(StringComparer.Ordinal);
        foreach (var (code, scope) in global.Grants)
        {
            if (scope == PermissionScope.Global)
            {
                all[code] = scope;
            }
        }

        if (IsMember)
        {
            foreach (var (code, scope) in projectRoleGrants)
            {
                if (!all.TryGetValue(code, out var existing) || scope > existing)
                {
                    all[code] = scope;
                }
            }
        }

        return all;
    }
}

/// <summary>Resolves <see cref="ProjectPermissions"/> for the current member.</summary>
public sealed class ProjectAccess(IAppDbContext db, ICurrentUser currentUser, PermissionService permissions)
{
    public async Task<ProjectPermissions> ForProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequiredUserId;
        var global = await permissions.GetAsync(userId, cancellationToken);
        var roleId = await db.ProjectMembers
            .AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.UserId == userId && m.RemovedAt == null)
            .Select(m => (Guid?)m.ProjectRoleId)
            .FirstOrDefaultAsync(cancellationToken);
        var grants = roleId is { } id
            ? await permissions.GetRoleGrantsAsync(id, cancellationToken)
            : new Dictionary<string, PermissionScope>();
        return new ProjectPermissions(projectId, roleId, global, grants);
    }

    /// <summary>
    /// Loads a project the member may see (<c>projects.view</c>), or null when it does not exist or is not visible —
    /// both answer 404 so project existence does not leak (docs/api-design.md §2).
    /// </summary>
    public async Task<(Project Project, ProjectPermissions Permissions)?> LoadVisibleAsync(
        Guid projectId, bool track, CancellationToken cancellationToken)
    {
        var permissionsHere = await ForProjectAsync(projectId, cancellationToken);
        if (!permissionsHere.HasAny(PermissionCatalog.ProjectsView))
        {
            return null;
        }

        var query = track ? db.Projects : db.Projects.AsNoTracking();
        var project = await query.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        return project is null ? null : (project, permissionsHere);
    }

    /// <summary>
    /// For list queries: the projects where the member holds <paramref name="code"/> through their project role,
    /// split by scope, plus whether a Global grant makes the lists irrelevant.
    /// </summary>
    public async Task<ScopedProjects> ProjectsWithAsync(string code, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequiredUserId;
        var global = await permissions.GetAsync(userId, cancellationToken);
        if (global.Has(code, PermissionScope.Global))
        {
            return new ScopedProjects(true, [], []);
        }

        var memberships = await db.ProjectMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.RemovedAt == null)
            .Select(m => new { m.ProjectId, m.ProjectRoleId })
            .ToListAsync(cancellationToken);

        var projectWide = new List<Guid>();
        var own = new List<Guid>();
        foreach (var membership in memberships)
        {
            var grants = await permissions.GetRoleGrantsAsync(membership.ProjectRoleId, cancellationToken);
            if (!grants.TryGetValue(code, out var scope))
            {
                continue;
            }

            (scope >= PermissionScope.Project ? projectWide : own).Add(membership.ProjectId);
        }

        return new ScopedProjects(false, projectWide, own);
    }
}

/// <summary>Where a member holds a permission: everywhere, in whole projects, or only for their own records in projects.</summary>
public sealed record ScopedProjects(bool Everywhere, IReadOnlyList<Guid> ProjectWide, IReadOnlyList<Guid> OwnOnly);
