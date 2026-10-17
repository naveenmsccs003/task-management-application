using TaskManagement.Domain.Common;

namespace TaskManagement.Domain.Access;

/// <summary>A configurable set of permissions (FR-ROLE-01). Seed roles are marked <see cref="IsSystem"/>.</summary>
public sealed class Role
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;

    private readonly List<RolePermission> _permissions = [];

    private Role()
    {
        Name = string.Empty;
        Description = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string Description { get; private set; }

    /// <summary>Seed roles cannot be deleted or renamed (FR-ROLE-03).</summary>
    public bool IsSystem { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Set on every change, so the role row's version changes even when only its permissions change.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public IReadOnlyCollection<RolePermission> Permissions => _permissions;

    public static Role CreateSystem(Guid id, string name, string description, DateTimeOffset now) =>
        new() { Id = id, Name = name, Description = description, IsSystem = true, CreatedAt = now, UpdatedAt = now };

    public static Role CreateCustom(string name, string description, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Role { Id = Guid.CreateVersion7(now), Name = name.Trim(), Description = description.Trim(), CreatedAt = now, UpdatedAt = now };
    }

    public Result Rename(string name, string description, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (IsSystem && !string.Equals(name.Trim(), Name, StringComparison.Ordinal))
        {
            return Error.Conflict("role.system_rename", "Built-in roles cannot be renamed.");
        }

        Name = name.Trim();
        Description = description.Trim();
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Replaces the role's grants. The Admin role must always keep the permissions needed to manage
    /// roles and create accounts, so the system can never lock itself out (FR-ROLE-03).
    /// </summary>
    public Result SetPermissions(IReadOnlyDictionary<string, PermissionScope> grants, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(grants);

        if (Id == SystemRoles.AdminId)
        {
            foreach (var required in PermissionCatalog.AdminProtected)
            {
                if (!grants.TryGetValue(required, out var scope) || scope != PermissionScope.Global)
                {
                    return Error.Conflict(
                        "role.admin_protected_permission",
                        $"The Admin role must keep '{required}' with Global scope.");
                }
            }
        }

        _permissions.RemoveAll(p => !grants.ContainsKey(p.PermissionCode));
        foreach (var (code, scope) in grants)
        {
            var existing = _permissions.Find(p => p.PermissionCode == code);
            if (existing is null)
            {
                _permissions.Add(new RolePermission(Id, code, scope));
            }
            else
            {
                existing.ChangeScope(scope);
            }
        }

        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Used by the seeder when a new permission code appears: grants its default without touching other grants.</summary>
    public void GrantIfMissing(string code, PermissionScope scope)
    {
        if (!_permissions.Exists(p => p.PermissionCode == code))
        {
            _permissions.Add(new RolePermission(Id, code, scope));
        }
    }
}
