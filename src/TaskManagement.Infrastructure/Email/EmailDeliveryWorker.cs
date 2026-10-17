using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using TaskManagement.Domain.Notifications;
using TaskManagement.Infrastructure.Persistence;

namespace TaskManagement.Infrastructure.Email;

/// <summary>
/// Sends queued emails with retries (FR-NOT-05). Rows are claimed with <c>FOR UPDATE SKIP LOCKED</c>, so several
/// app instances can run this worker without sending an email twice.
/// </summary>
public sealed partial class EmailDeliveryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<EmailOptions> options,
    TimeProvider timeProvider,
    ILogger<EmailDeliveryWorker> logger) : BackgroundService
{
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollInterval, timeProvider);
        do
        {
            try
            {
                await ProcessDueAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogBatchFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Sends every due email once. Also used by the <c>create-admin</c> command and by tests.</summary>
    public async Task<int> ProcessDueAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var transport = scope.ServiceProvider.GetRequiredService<IEmailTransport>();
        var now = timeProvider.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var due = await db.EmailDeliveries
            .FromSql($"""
                SELECT * FROM notifications.email_deliveries
                WHERE status = 'Pending' AND next_attempt_at <= {now}
                ORDER BY next_attempt_at
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        foreach (var delivery in due)
        {
            try
            {
                await transport.SendAsync(BuildMessage(delivery), cancellationToken);
                delivery.MarkSent(timeProvider.GetUtcNow());
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Log the type only: SMTP errors can echo addresses or server details.
                LogSendFailed(logger, delivery.Id, delivery.Attempts + 1, exception.GetType().Name);
                delivery.MarkAttemptFailed(exception.GetType().Name + ": " + exception.Message, timeProvider.GetUtcNow());
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return due.Count;
    }

    private MimeMessage BuildMessage(EmailDelivery delivery)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(options.Value.FromName, options.Value.FromAddress));
        message.To.Add(MailboxAddress.Parse(delivery.RecipientEmail));
        message.Subject = delivery.Subject;
        message.Body = new BodyBuilder { TextBody = delivery.BodyText, HtmlBody = delivery.BodyHtml }.ToMessageBody();
        return message;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Email delivery batch failed")]
    private static partial void LogBatchFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email {DeliveryId} attempt {Attempt} failed: {ErrorType}")]
    private static partial void LogSendFailed(ILogger logger, Guid deliveryId, int attempt, string errorType);
}
