using System.Net;
using System.Text.Json;
using TaskManagement.Domain.Access;
using TaskManagement.IntegrationTests.Infrastructure;

namespace TaskManagement.IntegrationTests.Administration;

[Collection(ApiCollection.Name)]
public sealed class RoleManagementTests(ApiFactory factory)
{
    private static async Task<JsonElement> CreateRoleAsync(TestClient admin) =>
        await (await admin.PostAsync("/api/v1/roles", new { name = $"QA {Guid.NewGuid():N}"[..20], description = "Testers" })).JsonAsync();

    private static Task<HttpResponseMessage> SetPermissionsAsync(TestClient admin, JsonElement role, params (string Code, string Scope)[] grants) =>
        admin.PutAsync($"/api/v1/roles/{role.GetProperty("id").GetGuid()}/permissions", new
        {
            permissions = grants.Select(g => new { code = g.Code, scope = g.Scope }).ToArray(),
            version = role.GetProperty("version").GetUInt32(),
        });

    [Fact]
    public async Task Seed_roles_and_all_catalog_permissions_exist()
    {
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);

        var roles = await admin.GetJsonAsync<JsonElement>("/api/v1/roles");
        var permissions = await admin.GetJsonAsync<JsonElement>("/api/v1/permissions");

        var names = roles.EnumerateArray().Select(r => r.GetProperty("name").GetString()).ToList();
        Assert.All(SystemRoles.All, r => Assert.Contains(r.Name, names));
        Assert.Equal(PermissionCatalog.All.Count, permissions.GetArrayLength());
    }

    [Fact]
    public async Task AC_ROLE_1_granting_and_removing_a_permission_takes_effect_immediately()
    {
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);
        var role = await CreateRoleAsync(admin);
        var member = await factory.CreateMemberAsync(role.GetProperty("id").GetGuid());
        using var client = factory.CreateTestClient();
        await client.SignInAsync(member.Email, member.Password);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/members")).StatusCode);

        var granted = await (await SetPermissionsAsync(admin, role, ("members.view", "Global"))).JsonAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/members")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await SetPermissionsAsync(admin, granted)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/members")).StatusCode);
    }

    [Fact]
    public async Task Admin_role_cannot_lose_protected_permissions()
    {
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);
        var adminRole = (await admin.GetJsonAsync<JsonElement>("/api/v1/roles")).EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == SystemRoles.AdminId);

        var response = await SetPermissionsAsync(admin, adminRole, ("audit.view", "Global"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("role.admin_protected_permission", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Unknown_permission_codes_are_rejected()
    {
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);
        var role = await CreateRoleAsync(admin);

        var response = await SetPermissionsAsync(admin, role, ("everything.allowed", "Global"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Concurrent_permission_edits_are_detected()
    {
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);
        var role = await CreateRoleAsync(admin);

        var first = await SetPermissionsAsync(admin, role, ("reports.view", "Own"));
        var second = await SetPermissionsAsync(admin, role, ("tasks.view", "Own"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task System_roles_and_roles_in_use_cannot_be_deleted()
    {
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);
        var role = await CreateRoleAsync(admin);
        await factory.CreateMemberAsync(role.GetProperty("id").GetGuid());
        var unused = await CreateRoleAsync(admin);

        var system = await admin.DeleteAsync($"/api/v1/roles/{SystemRoles.ViewerId}");
        var inUse = await admin.DeleteAsync($"/api/v1/roles/{role.GetProperty("id").GetGuid()}");
        var ok = await admin.DeleteAsync($"/api/v1/roles/{unused.GetProperty("id").GetGuid()}");

        Assert.Equal("role.system_delete", await system.ProblemCodeAsync());
        Assert.Equal("role.in_use", await inUse.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
    }

    [Fact]
    public async Task Non_admins_cannot_view_or_change_roles()
    {
        using var pm = await factory.SignedInClientAsync(SystemRoles.ProjectManagerId);

        Assert.Equal(HttpStatusCode.Forbidden, (await pm.GetAsync("/api/v1/roles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await pm.PostAsync("/api/v1/roles", new { name = "Sneaky", description = "" })).StatusCode);
    }
}
