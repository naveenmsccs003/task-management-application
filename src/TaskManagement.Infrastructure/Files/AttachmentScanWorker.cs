using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskManagement.Application.Attachments;
using TaskManagement.Application.Notifications;

namespace TaskManagement.Infrastructure.Files;

/// <summary>Runs <see cref="AttachmentScanning"/> every 10 seconds and records each run that found work.</summary>
public sealed partial class AttachmentScanWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<AttachmentScanWorker> logger)
    : BackgroundService
{
    public const string JobName = "attachment-scan";
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogRunFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Scans until nothing is pending (or a batch makes no progress); returns how many files were looked at.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var scanning = scope.ServiceProvider.GetRequiredService<AttachmentScanning>();
            var scanned = await scope.ServiceProvider.GetRequiredService<JobRecorder>()
                .RunAsync(JobName, () => scanning.ScanPendingAsync(cancellationToken), count => $"{count} files scanned", cancellationToken);
            total += scanned;
            if (scanned < AttachmentScanning.BatchSize)
            {
                return total;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The attachment scan run failed.")]
    private static partial void LogRunFailed(ILogger logger, Exception exception);
}

/// <summary>Readiness: the storage folder can be written to.</summary>
internal sealed class FileStorageHealthCheck(IFileStorage storage) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (storage is not LocalFileStorage local)
        {
            return HealthCheckResult.Healthy();
        }

        try
        {
            await local.ProbeAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return HealthCheckResult.Unhealthy("File storage is not writable.");
        }
    }
}

/// <summary>Readiness: clamd answers PING (only registered when scanning is on).</summary>
internal sealed class ClamAvHealthCheck(IMalwareScanner scanner) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (scanner is not ClamAvScanner clamAv)
        {
            return HealthCheckResult.Healthy();
        }

        try
        {
            return await clamAv.PingAsync(cancellationToken) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("ClamAV did not answer PING.");
        }
        catch (Exception exception) when (exception is IOException or System.Net.Sockets.SocketException or OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("ClamAV is not reachable.");
        }
    }
}
