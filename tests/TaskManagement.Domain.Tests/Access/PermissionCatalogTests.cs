using TaskManagement.Domain.Access;

namespace TaskManagement.Domain.Tests.Access;

/// <summary>Sanity checks on the seed data that encodes docs/permissions-matrix.md.</summary>
public sealed class PermissionCatalogTests
{
    [Fact]
    public void Codes_are_unique_and_short_enough_for_the_database()
    {
        var codes = PermissionCatalog.All.Select(e => e.Code).ToList();

        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.InRange(code.Length, 3, Permission.CodeMaxLength));
    }

    [Fact]
    public void Admin_defaults_include_the_protected_permissions_globally()
    {
        foreach (var code in PermissionCatalog.AdminProtected)
        {
            var entry = PermissionCatalog.All.Single(e => e.Code == code);
            Assert.Equal(PermissionScope.Global, entry.DefaultFor(SystemRoles.AdminId));
        }
    }

    [Fact]
    public void Only_admins_can_create_accounts_by_default()
    {
        var entry = PermissionCatalog.All.Single(e => e.Code == PermissionCatalog.MembersCreate);

        Assert.Null(entry.DefaultFor(SystemRoles.TeamLeaderId));
        Assert.Null(entry.DefaultFor(SystemRoles.ProjectManagerId));
        Assert.Null(entry.DefaultFor(SystemRoles.DeveloperId));
        Assert.Null(entry.DefaultFor(SystemRoles.ViewerId));
    }

    [Fact]
    public void Every_seed_role_can_manage_its_own_profile()
    {
        var entry = PermissionCatalog.All.Single(e => e.Code == PermissionCatalog.ProfileManage);

        Assert.All(SystemRoles.All, role => Assert.NotNull(entry.DefaultFor(role.Id)));
    }
}
