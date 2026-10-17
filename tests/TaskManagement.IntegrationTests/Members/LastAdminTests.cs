using System.Net;
using TaskManagement.Domain.Access;
using TaskManagement.IntegrationTests.Infrastructure;

namespace TaskManagement.IntegrationTests.Members;

/// <summary>AC-MEM-2. Uses its own database so "the last Admin" is well defined.</summary>
public sealed class LastAdminTests : IAsyncLifetime
{
    private readonly ApiFactory _factory = new();

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task AC_MEM_2_the_last_active_admin_cannot_be_deactivated_or_demoted()
    {
        var onlyAdmin = await _factory.CreateMemberAsync(SystemRoles.AdminId);
        using var admin = _factory.CreateTestClient();
        await admin.SignInAsync(onlyAdmin.Email, onlyAdmin.Password);

        // Give a custom "Member Manager" role the right to update members, so someone other than the admin can try.
        var role = await (await admin.PostAsync("/api/v1/roles", new { name = "Member Manager", description = "" })).JsonAsync();
        var roleId = role.GetProperty("id").GetGuid();
        var grant = await admin.PutAsync($"/api/v1/roles/{roleId}/permissions", new
        {
            permissions = new[] { new { code = "members.update", scope = "Global" }, new { code = "members.view", scope = "Global" } },
            version = role.GetProperty("version").GetUInt32(),
        });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);

        var manager = await _factory.CreateMemberAsync(roleId);
        using var managerClient = _factory.CreateTestClient();
        await managerClient.SignInAsync(manager.Email, manager.Password);

        var deactivate = await managerClient.PostAsync($"/api/v1/members/{onlyAdmin.Id}/deactivate");
        var loaded = await managerClient.GetJsonAsync<System.Text.Json.JsonElement>($"/api/v1/members/{onlyAdmin.Id}");
        var demote = await managerClient.PatchAsync($"/api/v1/members/{onlyAdmin.Id}", new
        {
            fullName = "Admin",
            timeZone = "UTC",
            employeeCode = (string?)null,
            globalRoleId = SystemRoles.ViewerId,
            version = loaded.GetProperty("version").GetUInt32(),
        });

        Assert.Equal(HttpStatusCode.Conflict, deactivate.StatusCode);
        Assert.Equal("member.last_admin", await deactivate.ProblemCodeAsync());
        Assert.Equal("member.last_admin", await demote.ProblemCodeAsync());

        // With a second admin, deactivation is allowed.
        await _factory.CreateMemberAsync(SystemRoles.AdminId);
        Assert.Equal(HttpStatusCode.OK, (await managerClient.PostAsync($"/api/v1/members/{onlyAdmin.Id}/deactivate")).StatusCode);
    }
}
