using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaskManagement.Application.Identity;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Identity;
using TaskManagement.Infrastructure.Email;
using TaskManagement.Infrastructure.Persistence;

namespace TaskManagement.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real API in memory against a real, migrated PostgreSQL database, in the Testing environment
/// (so production-style cookies, error handling and JSON logging are exercised, not the Development ones).
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string DefaultPassword = "river-copper-lantern-42";

    private PostgresDatabase? _database;

    public string ConnectionString => _database?.ConnectionString
        ?? throw new InvalidOperationException("The database has not been started yet.");

    public FakeEmailTransport Emails { get; } = new();

    /// <summary>Optional test-specific service replacements, applied after the app's own registrations.</summary>
    public Action<IServiceCollection>? ConfigureServices { get; init; }

    /// <summary>Optional configuration overrides (e.g. low rate limits for a rate-limit test).</summary>
    public IDictionary<string, string?> Settings { get; init; } = new Dictionary<string, string?>();

    public async ValueTask InitializeAsync()
    {
        _database = await PostgresDatabase.CreateAsync();

        // Migrate before the app starts: the app seeds roles and permissions at start-up.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "ops"))
            .UseSnakeCaseNamingConvention()
            .Options;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        _ = Services; // start the host
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Auth:SignInPermitsPerMinute", "10000");
        builder.UseSetting("Auth:PasswordPermitsPerQuarterHour", "10000");
        builder.UseSetting("App:PublicBaseUrl", "https://tma.test");
        builder.UseSetting("Email:BackgroundWorkerEnabled", "false"); // tests call FlushEmailsAsync instead
        builder.UseSetting("Jobs:SessionMonitorEnabled", "false"); // tests run the monitor explicitly
        builder.UseSetting("Jobs:CleanupEnabled", "false");
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailTransport>();
            services.AddSingleton<IEmailTransport>(Emails);
            ConfigureServices?.Invoke(services);
        });
    }

    /// <summary>A client that keeps cookies and talks HTTPS (required for <c>__Host-</c> cookies).</summary>
    public TestClient CreateTestClient() =>
        new(CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true }));

    /// <summary>Creates an active member with a password directly in the database (bypassing the invite email).</summary>
    public async Task<TestMember> CreateMemberAsync(Guid roleId, string? password = DefaultPassword, string? email = null)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        email ??= $"member-{Guid.NewGuid():N}@example.com";
        var user = User.Create(email, "Test " + RoleName(roleId), "UTC", null, roleId, null, DateTimeOffset.UtcNow);
        if (password is not null)
        {
            user.SetPasswordHash(hasher.Hash(password), DateTimeOffset.UtcNow);
        }

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return new TestMember(user.Id, email, password ?? string.Empty);
    }

    public async Task<TestClient> SignedInClientAsync(Guid roleId)
    {
        var member = await CreateMemberAsync(roleId);
        var client = CreateTestClient();
        await client.SignInAsync(member.Email, member.Password);
        return client;
    }

    /// <summary>Runs the email worker until no due email is left, so queued emails reach <see cref="Emails"/>.</summary>
    public async Task FlushEmailsAsync()
    {
        var worker = Services.GetRequiredService<EmailDeliveryWorker>();
        while (await worker.ProcessDueAsync(CancellationToken.None) > 0)
        {
        }
    }

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    private static string RoleName(Guid roleId) => SystemRoles.All.FirstOrDefault(r => r.Id == roleId).Name ?? "Member";
}

public sealed record TestMember(Guid Id, string Email, string Password);
