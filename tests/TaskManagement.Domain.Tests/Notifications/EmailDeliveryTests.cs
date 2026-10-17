using TaskManagement.Domain.Notifications;

namespace TaskManagement.Domain.Tests.Notifications;

public sealed class EmailDeliveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Failed_attempts_back_off_exponentially_then_give_up_and_wipe_secrets()
    {
        var email = EmailDelivery.Queue("a@example.com", "Reset", "link: secret", "<a>secret</a>", containsSecret: true, Now);

        email.MarkAttemptFailed("timeout", Now);
        Assert.Equal(Now.AddSeconds(30), email.NextAttemptAt);
        email.MarkAttemptFailed("timeout", Now);
        Assert.Equal(Now.AddSeconds(60), email.NextAttemptAt);

        for (var i = 2; i < EmailDelivery.MaxAttempts; i++)
        {
            email.MarkAttemptFailed("timeout", Now);
        }

        Assert.Equal(EmailDeliveryStatus.Failed, email.Status);
        Assert.DoesNotContain("secret", email.BodyText, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", email.BodyHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void Sent_email_with_secret_is_wiped_but_ordinary_email_is_kept()
    {
        var secret = EmailDelivery.Queue("a@example.com", "Reset", "token", "token", containsSecret: true, Now);
        var ordinary = EmailDelivery.Queue("a@example.com", "Assigned", "TMA-1", "TMA-1", containsSecret: false, Now);

        secret.MarkSent(Now);
        ordinary.MarkSent(Now);

        Assert.DoesNotContain("token", secret.BodyText, StringComparison.Ordinal);
        Assert.Equal("TMA-1", ordinary.BodyText);
        Assert.Equal(EmailDeliveryStatus.Sent, ordinary.Status);
    }

    [Fact]
    public void Long_errors_are_truncated()
    {
        var email = EmailDelivery.Queue("a@example.com", "s", "b", "b", containsSecret: false, Now);

        email.MarkAttemptFailed(new string('x', 5000), Now);

        Assert.Equal(1000, email.LastError!.Length);
    }
}
