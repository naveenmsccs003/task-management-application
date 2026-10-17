using Microsoft.EntityFrameworkCore;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Notifications;
using TaskManagement.IntegrationTests.Infrastructure;

namespace TaskManagement.IntegrationTests.Platform;

[Collection(ApiCollection.Name)]
public sealed class EmailDeliveryTests(ApiFactory factory)
{
    [Fact]
    public async Task Failed_send_is_retried_later_and_the_business_change_is_kept()
    {
        var member = await factory.CreateMemberAsync(SystemRoles.DeveloperId);
        using var client = factory.CreateTestClient();
        await client.RefreshAntiforgeryAsync();
        await client.PostAsync("/api/v1/auth/forgot-password", new { email = member.Email });

        factory.Emails.FailNextFor(member.Email);
        await factory.FlushEmailsAsync();

        var delivery = await factory.WithDbAsync(db => db.EmailDeliveries.AsNoTracking().SingleAsync(e => e.RecipientEmail == member.Email));
        Assert.Equal(EmailDeliveryStatus.Pending, delivery.Status);
        Assert.Equal(1, delivery.Attempts);
        Assert.True(delivery.NextAttemptAt > DateTimeOffset.UtcNow);
        Assert.Contains("Simulated SMTP failure", delivery.LastError, StringComparison.Ordinal);
        Assert.True(await factory.WithDbAsync(db => db.PasswordTokens.AnyAsync(t => t.UserId == member.Id)));

        await factory.WithDbAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE notifications.email_deliveries SET next_attempt_at = now() WHERE id = {delivery.Id}"));
        await factory.FlushEmailsAsync();

        var sent = await factory.WithDbAsync(db => db.EmailDeliveries.AsNoTracking().SingleAsync(e => e.Id == delivery.Id));
        Assert.Equal(EmailDeliveryStatus.Sent, sent.Status);
        Assert.Single(factory.Emails.To(member.Email));
    }
}
