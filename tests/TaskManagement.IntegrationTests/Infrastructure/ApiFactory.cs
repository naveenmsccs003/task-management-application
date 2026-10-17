using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskManagement.Infrastructure.Persistence;

namespace TaskManagement.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real API in memory against a real, migrated PostgreSQL database, in the Testing environment
/// (so production-style error handling and JSON logging are exercised, not the Development ones).
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private PostgresDatabase? _database;

    public string ConnectionString => _database?.ConnectionString
        ?? throw new InvalidOperationException("The database has not been started yet.");

    /// <summary>Optional test-specific service replacements, applied after the app's own registrations.</summary>
    public Action<IServiceCollection>? ConfigureServices { get; init; }

    public async ValueTask InitializeAsync()
    {
        _database = await PostgresDatabase.CreateAsync();

        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        if (ConfigureServices is not null)
        {
            builder.ConfigureServices(ConfigureServices);
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }
}
