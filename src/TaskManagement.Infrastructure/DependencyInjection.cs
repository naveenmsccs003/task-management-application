using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaskManagement.Application.Common;
using TaskManagement.Application.Identity;
using TaskManagement.Infrastructure.Email;
using TaskManagement.Infrastructure.Identity;
using TaskManagement.Infrastructure.Persistence;
using TaskManagement.Infrastructure.Seeding;

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
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<DataSeeder>();

        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddScoped<ISignInAttemptStore, SignInAttemptStore>();

        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.Section));
        var transport = configuration.GetValue(EmailOptions.Section + ":Transport", EmailTransportKind.Smtp);
        if (transport == EmailTransportKind.PickupDirectory)
        {
            services.TryAddSingleton<IEmailTransport, PickupDirectoryEmailTransport>();
        }
        else
        {
            services.TryAddSingleton<IEmailTransport, SmtpEmailTransport>();
        }

        services.AddSingleton<EmailDeliveryWorker>();
        if (configuration.GetValue(EmailOptions.Section + ":BackgroundWorkerEnabled", true))
        {
            services.AddHostedService(sp => sp.GetRequiredService<EmailDeliveryWorker>());
        }

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

        return services;
    }
}
