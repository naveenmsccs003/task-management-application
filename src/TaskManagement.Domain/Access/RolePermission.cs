namespace TaskManagement.Domain.Access;

public sealed class RolePermission
{
    private RolePermission()
    {
        PermissionCode = string.Empty;
    }

    internal RolePermission(Guid roleId, string permissionCode, PermissionScope scope)
    {
        RoleId = roleId;
        PermissionCode = permissionCode;
        Scope = scope;
    }

    public Guid RoleId { get; private set; }

    public string PermissionCode { get; private set; }

    public PermissionScope Scope { get; private set; }

    internal void ChangeScope(PermissionScope scope) => Scope = scope;
}
