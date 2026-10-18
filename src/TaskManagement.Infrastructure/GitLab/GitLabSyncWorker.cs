using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskManagement.Application.Common;
using TaskManagement.Application.GitLab;
using TaskManagement.Application.Notifications;
using TaskManagement.Domain.GitLab;

namespace TaskManagement.Infrastructure.GitLab;

/// <summary>
/// The scheduled GitLab sync (FR-IMP-06, default every 15 minutes): each linked project with sync on is synced in its
/// own scope, so one failing project (GitLab down, token revoked) does not stop the others (FR-IMP-09).
/// </summary>
public sealed partial class GitLabSyncWorker(
    IServiceScopeFactory scopeFactory, IOptions<GitLabOptions> options, TimeProvider timeProvider, ILogger<GitLabSyncWorker> logger) : BackgroundService
{
    public const string JobName = "gitlab-sync";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.SyncInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
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
    }

    /// <summary>Returns how many projects were synced without failures.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        List<Guid> linkIds;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            linkIds = await db.GitLabProjectLinks.AsNoTracking().Where(l => l.SyncEnabled).Select(l => l.Id).ToListAsync(cancellationToken);
        }

        await using var recordScope = scopeFactory.CreateAsyncScope();
        return await recordScope.ServiceProvider.GetRequiredService<JobRecorder>().RunAsync(
            JobName,
            async () =>
            {
                var succeeded = 0;
                foreach (var linkId in linkIds)
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    try
                    {
                        var run = await scope.ServiceProvider.GetRequiredService<GitLabImports>()
                            .SyncLinkAsync(linkId, ImportRunKind.Sync, null, null, cancellationToken);
                        succeeded += run.FailedCount == 0 ? 1 : 0;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        LogProjectFailed(logger, exception, linkId);
                    }
                }

                return succeeded;
            },
            succeeded => $"{succeeded} of {linkIds.Count} linked projects synced",
            cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The GitLab sync run failed.")]
    private static partial void LogRunFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "GitLab sync of project link {LinkId} failed.")]
    private static partial void LogProjectFailed(ILogger logger, Exception exception, Guid linkId);
}
