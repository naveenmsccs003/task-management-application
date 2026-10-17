using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Access;
using TaskManagement.Domain.Access;
using TaskManagement.Infrastructure.Persistence;

namespace TaskManagement.Infrastructure.Seeding;

/// <summary>
/// Idempotent startup seeding (docs/database-design.md §6): inserts missing permission codes and system roles,
/// and grants a new permission's seed-role defaults only when that permission is first inserted. It never
/// overwrites grants an Admin has changed.
/// </summary>
public sealed class DataSeeder(AppDbContext db, TimeProvider timeProvider)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var roles = await db.Roles.Include(r => r.Permissions).ToDictionaryAsync(r => r.Id, cancellationToken);
        foreach (var (id, name, description) in SystemRoles.All)
        {
            if (!roles.ContainsKey(id))
            {
                var role = Role.CreateSystem(id, name, description, now);
                db.Roles.Add(role);
                roles[id] = role;
            }
        }

        var existingPermissions = await db.Permissions.ToDictionaryAsync(p => p.Code, StringComparer.Ordinal, cancellationToken);
        foreach (var entry in PermissionCatalog.All)
        {
            if (existingPermissions.TryGetValue(entry.Code, out var permission))
            {
                permission.UpdateDescription(entry.Module, entry.Description);
                continue;
            }

            db.Permissions.Add(new Permission(entry.Code, entry.Module, entry.Description));
            foreach (var (roleId, _, _) in SystemRoles.All)
            {
                if (entry.DefaultFor(roleId) is { } scope)
                {
                    roles[roleId].GrantIfMissing(entry.Code, scope);
                }
            }
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(cancellationToken);
            PermissionService.InvalidateAll();
        }
    }
}
