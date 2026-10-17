using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskManagement.Api.Cli;
using TaskManagement.Domain.Access;
using TaskManagement.Infrastructure.Seeding;
using TaskManagement.IntegrationTests.Infrastructure;

namespace TaskManagement.IntegrationTests.Platform;

[Collection(ApiCollection.Name)]
public sealed class StartupTests(ApiFactory factory)
{
    [Fact]
    public async Task Seeding_is_idempotent_and_keeps_admin_changes()
    {
        await factory.WithDbAsync(async db =>
        {
            var viewer = await db.Roles.Include(r => r.Permissions).SingleAsync(r => r.Id == SystemRoles.ViewerId);
            var grants = viewer.Permissions.Where(p => p.PermissionCode != "reports.view").ToDictionary(p => p.PermissionCode, p => p.Scope);
            viewer.SetPermissions(grants, DateTimeOffset.UtcNow);
            return await db.SaveChangesAsync();
        });

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync(CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync(CancellationToken.None);
        }

        var (permissionCount, roleCount, viewerHasReports) = await factory.WithDbAsync(async db => (
            await db.Permissions.CountAsync(),
            await db.Roles.CountAsync(r => r.IsSystem),
            await db.Roles.Where(r => r.Id == SystemRoles.ViewerId).SelectMany(r => r.Permissions).AnyAsync(p => p.PermissionCode == "reports.view")));
        Assert.Equal(PermissionCatalog.All.Count, permissionCount);
        Assert.Equal(SystemRoles.All.Count, roleCount);
        Assert.False(viewerHasReports, "The seeder must not restore a grant an Admin removed.");

        // Restore for other tests.
        await factory.WithDbAsync(async db =>
        {
            var viewer = await db.Roles.Include(r => r.Permissions).SingleAsync(r => r.Id == SystemRoles.ViewerId);
            viewer.GrantIfMissing("reports.view", PermissionScope.Project);
            return await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task Create_admin_command_creates_an_admin_and_sends_the_invite()
    {
        var email = $"first-admin-{Guid.NewGuid():N}@example.com";

        var exitCode = await CreateAdminCommand.RunAsync(factory.Services, ["create-admin", "--email", email, "--name", "First Admin", "--time-zone", "Asia/Kolkata"]);

        Assert.Equal(0, exitCode);
        var user = await factory.WithDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Email == email));
        Assert.Equal(SystemRoles.AdminId, user.GlobalRoleId);
        Assert.Null(user.PasswordHash);
        Assert.Contains("/set-password?token=", Assert.Single(factory.Emails.To(email)).TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_admin_command_reports_bad_input()
    {
        Assert.Equal(2, await CreateAdminCommand.RunAsync(factory.Services, ["create-admin", "--email", "a@example.com"]));
        Assert.Equal(1, await CreateAdminCommand.RunAsync(factory.Services, ["create-admin", "--email", "bad", "--name", "X"]));
    }
}
