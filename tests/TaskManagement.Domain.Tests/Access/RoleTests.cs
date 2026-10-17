using TaskManagement.Domain.Access;

namespace TaskManagement.Domain.Tests.Access;

public sealed class RoleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    private static Dictionary<string, PermissionScope> AdminMinimum() => new()
    {
        [PermissionCatalog.RolesManage] = PermissionScope.Global,
        [PermissionCatalog.MembersCreate] = PermissionScope.Global,
    };

    [Theory]
    [InlineData(PermissionCatalog.RolesManage)]
    [InlineData(PermissionCatalog.MembersCreate)]
    public void Admin_role_cannot_lose_protected_permissions(string protectedCode)
    {
        var admin = Role.CreateSystem(SystemRoles.AdminId, "Admin", "", Now);
        var grants = AdminMinimum();
        grants.Remove(protectedCode);

        var result = admin.SetPermissions(grants, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("role.admin_protected_permission", result.Error!.Code);
    }

    [Fact]
    public void Admin_protected_permissions_must_stay_global()
    {
        var admin = Role.CreateSystem(SystemRoles.AdminId, "Admin", "", Now);
        var grants = AdminMinimum();
        grants[PermissionCatalog.RolesManage] = PermissionScope.Project;

        Assert.True(admin.SetPermissions(grants, Now).IsFailure);
    }

    [Fact]
    public void Set_permissions_adds_changes_and_removes_grants()
    {
        var role = Role.CreateCustom("QA", "", Now);
        role.SetPermissions(new Dictionary<string, PermissionScope> { ["tasks.view"] = PermissionScope.Own, ["reports.view"] = PermissionScope.Own }, Now);

        var later = Now.AddMinutes(5);
        var result = role.SetPermissions(new Dictionary<string, PermissionScope> { ["tasks.view"] = PermissionScope.Project }, later);

        Assert.True(result.IsSuccess);
        var grant = Assert.Single(role.Permissions);
        Assert.Equal(("tasks.view", PermissionScope.Project), (grant.PermissionCode, grant.Scope));
        Assert.Equal(later, role.UpdatedAt);
    }

    [Fact]
    public void System_roles_cannot_be_renamed_but_description_can_change()
    {
        var role = Role.CreateSystem(SystemRoles.ViewerId, "Viewer", "old", Now);

        Assert.True(role.Rename("Watcher", "", Now).IsFailure);
        Assert.True(role.Rename("Viewer", "new description", Now).IsSuccess);
        Assert.Equal("new description", role.Description);
    }

    [Fact]
    public void Grant_if_missing_does_not_change_existing_grant()
    {
        var role = Role.CreateCustom("QA", "", Now);
        role.SetPermissions(new Dictionary<string, PermissionScope> { ["tasks.view"] = PermissionScope.Own }, Now);

        role.GrantIfMissing("tasks.view", PermissionScope.Global);

        Assert.Equal(PermissionScope.Own, Assert.Single(role.Permissions).Scope);
    }
}
