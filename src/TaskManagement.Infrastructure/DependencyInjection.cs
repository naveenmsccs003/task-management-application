using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaskManagement.Infrastructure.Persistence;

namespace TaskManagement.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Default";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Read lazily so tests and hosts can supply the connection string after registration.
        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            var connectionString = serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Connection string '{ConnectionStringName}' is not configured. Set ConnectionStrings__{ConnectionStringName} " +
                    "as an environment variable or with 'dotnet user-secrets' (see README.md).");
            }

            DbContextOptionsSetup.Configure(options, connectionString);
        });

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

        return services;
    }
}
