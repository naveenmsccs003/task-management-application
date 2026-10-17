using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskManagement.Infrastructure.Persistence;

namespace TaskManagement.IntegrationTests.Infrastructure;

[Collection(ApiCollection.Name)]
public sealed class DatabaseMigrationTests(ApiFactory factory)
{
    [Fact]
    public async Task All_migrations_are_applied()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pending = await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken);

        Assert.Empty(pending);
        Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Model_has_no_changes_missing_from_migrations()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(db.Database.HasPendingModelChanges(), "Run 'dotnet ef migrations add <Name>' to capture model changes.");
    }

    [Fact]
    public async Task Tables_use_module_schemas_and_snake_case()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var columns = await db.Database
            .SqlQuery<string>($"SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_schema = 'settings' AND table_name = 'global_settings' ORDER BY ordinal_position")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["key", "value", "updated_at", "updated_by"], columns);
    }
}
