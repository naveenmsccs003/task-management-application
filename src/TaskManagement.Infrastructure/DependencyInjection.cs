using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TaskManagement.Application.Attachments;
using TaskManagement.Application.Common;
using TaskManagement.Application.GitLab;
using TaskManagement.Application.Identity;
using TaskManagement.Application.Notifications;
using TaskManagement.Infrastructure.Email;
using TaskManagement.Infrastructure.Files;
using TaskManagement.Infrastructure.GitLab;
using TaskManagement.Infrastructure.Identity;
using TaskManagement.Infrastructure.Jobs;
using TaskManagement.Infrastructure.Notifications;
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
            options.AddInterceptors(serviceProvider.GetRequiredService<NotificationPublishingInterceptor>());
        });
        services.TryAddSingleton<INotificationPublisher, NullNotificationPublisher>();
        services.AddScoped<NotificationPublishingInterceptor>();
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

        services.AddSingleton<CleanupWorker>();
        if (configuration.GetValue("Jobs:CleanupEnabled", true))
        {
            services.AddHostedService(sp => sp.GetRequiredService<CleanupWorker>());
        }

        services.AddSingleton<DueDateWorker>();
        if (configuration.GetValue("Jobs:DueDatesEnabled", true))
        {
            services.AddHostedService(sp => sp.GetRequiredService<DueDateWorker>());
        }

        services.AddSingleton<SessionMonitorWorker>();
        if (configuration.GetValue("Jobs:SessionMonitorEnabled", true))
        {
            services.AddHostedService(sp => sp.GetRequiredService<SessionMonitorWorker>());
        }

        AddAttachments(services, configuration);
        AddGitLab(services, configuration);

        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database", tags: ["ready"])
            .AddCheck<FileStorageHealthCheck>("file-storage", tags: ["ready"])
            .AddCheck<ClamAvHealthCheck>("malware-scanner", tags: ["ready"]);

        return services;
    }

    /// <summary>GitLab client with the SSRF guard (security.md S19), and the scheduled sync.</summary>
    private static void AddGitLab(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(GitLabOptions.Section);
        services.Configure<GitLabOptions>(section);
        services.AddHttpClient(GitLabHttpClient.ClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("TaskManagement/1.0");
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var options = sp.GetRequiredService<IOptions<GitLabOptions>>().Value;
                return new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false,
                    ConnectCallback = (context, cancellationToken) => GitLabHttpClient.ConnectAsync(context, options, cancellationToken),
                };
            });
        services.TryAddSingleton<IGitLabClient, GitLabHttpClient>();

        services.AddSingleton<GitLabSyncWorker>();
        if (section.GetValue(nameof(GitLabOptions.SyncWorkerEnabled), true))
        {
            services.AddHostedService(sp => sp.GetRequiredService<GitLabSyncWorker>());
        }
    }

    /// <summary>Private file storage and malware scanning (docs/deployment.md, Q9).</summary>
    private static void AddAttachments(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(AttachmentOptions.Section);
        services.Configure<AttachmentOptions>(section);
        services.TryAddSingleton<IFileStorage>(sp =>
        {
            var path = sp.GetRequiredService<IOptions<AttachmentOptions>>().Value.LocalPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException(
                    $"{AttachmentOptions.Section}:LocalPath is not configured. Set it to a private folder outside the web root (see docs/deployment.md).");
            }

            return new LocalFileStorage(Path.Combine(sp.GetRequiredService<IHostEnvironment>().ContentRootPath, path));
        });

        if (section.GetValue(nameof(AttachmentOptions.Scanner), MalwareScannerKind.ClamAv) == MalwareScannerKind.None)
        {
            services.TryAddSingleton<IMalwareScanner, DisabledMalwareScanner>();
        }
        else
        {
            services.TryAddSingleton<IMalwareScanner, ClamAvScanner>();
        }

        services.AddSingleton<AttachmentScanWorker>();
        if (section.GetValue(nameof(AttachmentOptions.ScanWorkerEnabled), true))
        {
            services.AddHostedService(sp => sp.GetRequiredService<AttachmentScanWorker>());
        }
    }
}
