using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskManagement.Application.Work;

namespace TaskManagement.Infrastructure.Jobs;

/// <summary>
/// Runs <see cref="SessionMonitor"/> once a minute (task-workflow.md §4.6). Every step is a guarded database update, so
/// several app instances running it at once only repeat no-op work.
/// </summary>
public sealed partial class SessionMonitorWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<SessionMonitorWorker> logger)
    : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

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

    /// <summary>One pass in its own scope; also used by tests.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SessionMonitor>().RunOnceAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The session monitor run failed.")]
    private static partial void LogRunFailed(ILogger logger, Exception exception);
}
