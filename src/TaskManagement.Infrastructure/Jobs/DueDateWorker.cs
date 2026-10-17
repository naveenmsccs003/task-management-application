using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskManagement.Application.Deadlines;
using TaskManagement.Application.Notifications;

namespace TaskManagement.Infrastructure.Jobs;

/// <summary>
/// Runs <see cref="DueDateMonitor"/> every hour, so overdue flags and notices follow each member's local midnight within an
/// hour (FR-DL-05), and records the run for the system status page.
/// </summary>
public sealed partial class DueDateWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<DueDateWorker> logger)
    : BackgroundService
{
    public const string JobName = "due-date-monitor";
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
        var monitor = scope.ServiceProvider.GetRequiredService<DueDateMonitor>();
        return await scope.ServiceProvider.GetRequiredService<JobRecorder>()
            .RunAsync(JobName, () => monitor.RunOnceAsync(cancellationToken), changed => $"{changed} tasks updated or notified", cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The due-date run failed.")]
    private static partial void LogRunFailed(ILogger logger, Exception exception);
}
