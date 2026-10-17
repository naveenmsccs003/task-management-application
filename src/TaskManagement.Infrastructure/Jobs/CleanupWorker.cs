using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskManagement.Application.Notifications;

namespace TaskManagement.Infrastructure.Jobs;

/// <summary>Runs <see cref="RetentionCleanup"/> every hour and records the run for the system status page.</summary>
public sealed partial class CleanupWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<CleanupWorker> logger)
    : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

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

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var cleanup = scope.ServiceProvider.GetRequiredService<RetentionCleanup>();
        return await scope.ServiceProvider.GetRequiredService<JobRecorder>()
            .RunAsync(JobRecorder.Cleanup, () => cleanup.RunOnceAsync(cancellationToken), removed => $"{removed} old rows removed", cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The clean-up run failed.")]
    private static partial void LogRunFailed(ILogger logger, Exception exception);
}
