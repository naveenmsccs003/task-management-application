using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Application.Identity;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Common;

namespace TaskManagement.Application.Access;

public sealed record RoleDto(
    Guid Id, string Name, string Description, bool IsSystem, int MemberCount, IReadOnlyList<PermissionGrantDto> Permissions, uint Version);

public sealed record PermissionDto(string Code, string Module, string Description);

public sealed record SaveRoleCommand(string? Name, string? Description);

public sealed record SetRolePermissionsCommand(IReadOnlyList<PermissionGrantDto>? Permissions, uint Version);

/// <summary>Roles and their permissions (FR-ROLE-01..03, FR-ROLE-09).</summary>
public sealed class RoleManagement(IAppDbContext db, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken)
    {
        var roles = await db.Roles.AsNoTracking().Include(r => r.Permissions).OrderBy(r => r.Name).ToListAsync(cancellationToken);
        var counts = await db.Users
            .GroupBy(u => u.GlobalRoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count, cancellationToken);
        return roles.Select(r => ToDto(r, counts.GetValueOrDefault(r.Id))).ToList();
    }

    public async Task<IReadOnlyList<PermissionDto>> ListPermissionsAsync(CancellationToken cancellationToken) =>
        await db.Permissions
            .AsNoTracking()
            .OrderBy(p => p.Module)
            .ThenBy(p => p.Code)
            .Select(p => new PermissionDto(p.Code, p.Module, p.Description))
            .ToListAsync(cancellationToken);

    public async Task<Result<RoleDto>> CreateAsync(SaveRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = Validate(command);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        if (await NameTakenAsync(command.Name!, null, cancellationToken))
        {
            return Error.Conflict("role.name_taken", "A role with this name already exists.");
        }

        var role = Role.CreateCustom(command.Name!, command.Description ?? string.Empty, timeProvider.GetUtcNow());
        db.Roles.Add(role);
        auditLog.Record("role.created", "Role", role.Id.ToString(), new AuditChanges().Set("name", role.Name));
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(role, 0);
    }

    public async Task<Result<RoleDto>> UpdateAsync(Guid id, SaveRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = Validate(command);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (role is null)
        {
            return CommonErrors.NotFound("Role");
        }

        if (await NameTakenAsync(command.Name!, id, cancellationToken))
        {
            return Error.Conflict("role.name_taken", "A role with this name already exists.");
        }

        var changes = new AuditChanges()
            .Change("name", role.Name, command.Name!.Trim())
            .Change("description", role.Description, (command.Description ?? string.Empty).Trim());
        var result = role.Rename(command.Name, command.Description ?? string.Empty, timeProvider.GetUtcNow());
        if (result.IsFailure)
        {
            return result.Error!;
        }

        if (!changes.IsEmpty)
        {
            auditLog.Record("role.updated", "Role", role.Id.ToString(), changes);
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToDto(role, await db.Users.CountAsync(u => u.GlobalRoleId == id, cancellationToken));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (role is null)
        {
            return CommonErrors.NotFound("Role");
        }

        if (role.IsSystem)
        {
            return Error.Conflict("role.system_delete", "Built-in roles cannot be deleted.");
        }

        if (await db.Users.AnyAsync(u => u.GlobalRoleId == id, cancellationToken))
        {
            return Error.Conflict("role.in_use", "This role is assigned to members. Change their role first.");
        }

        db.Roles.Remove(role);
        auditLog.Record("role.deleted", "Role", role.Id.ToString(), new AuditChanges().Set("name", role.Name));
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<RoleDto>> SetPermissionsAsync(Guid id, SetRolePermissionsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (role is null)
        {
            return CommonErrors.NotFound("Role");
        }

        db.ExpectVersion(role, command.Version);

        var requested = command.Permissions ?? [];
        var known = await db.Permissions.Select(p => p.Code).ToListAsync(cancellationToken);
        var errors = new FieldErrors();
        foreach (var grant in requested)
        {
            if (!known.Contains(grant.Code, StringComparer.Ordinal))
            {
                errors.Add("permissions", $"Unknown permission '{grant.Code}'.");
            }

            if (!Enum.IsDefined(grant.Scope))
            {
                errors.Add("permissions", $"Unknown scope for '{grant.Code}'.");
            }
        }

        if (requested.GroupBy(g => g.Code, StringComparer.Ordinal).Any(g => g.Count() > 1))
        {
            errors.Add("permissions", "Each permission may appear only once.");
        }

        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var before = role.Permissions.ToDictionary(p => p.PermissionCode, p => p.Scope, StringComparer.Ordinal);
        var after = requested.ToDictionary(g => g.Code, g => g.Scope, StringComparer.Ordinal);
        var result = role.SetPermissions(after, timeProvider.GetUtcNow());
        if (result.IsFailure)
        {
            return result.Error!;
        }

        var changes = new AuditChanges();
        foreach (var code in before.Keys.Union(after.Keys).Order(StringComparer.Ordinal))
        {
            changes.Change(code, before.TryGetValue(code, out var b) ? b.ToString() : null, after.TryGetValue(code, out var a) ? a.ToString() : null);
        }

        if (!changes.IsEmpty)
        {
            auditLog.Record("role.permissions_changed", "Role", role.Id.ToString(), changes);
        }

        await db.SaveChangesAsync(cancellationToken);
        PermissionService.InvalidateAll();
        return ToDto(role, await db.Users.CountAsync(u => u.GlobalRoleId == id, cancellationToken));
    }

    private static FieldErrors Validate(SaveRoleCommand command) =>
        new FieldErrors()
            .Required("name", command.Name, "Name")
            .MaxLength("name", command.Name?.Trim(), Role.NameMaxLength, "Name")
            .MaxLength("description", command.Description?.Trim(), Role.DescriptionMaxLength, "Description");

    private Task<bool> NameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var normalized = name.Trim().ToLowerInvariant();
        return db.Roles.AnyAsync(r => r.Name.ToLower() == normalized && r.Id != exceptId, cancellationToken);
    }

    private static RoleDto ToDto(Role role, int memberCount) =>
        new(
            role.Id,
            role.Name,
            role.Description,
            role.IsSystem,
            memberCount,
            role.Permissions.OrderBy(p => p.PermissionCode, StringComparer.Ordinal).Select(p => new PermissionGrantDto(p.PermissionCode, p.Scope)).ToList(),
            role.Version);
}
