using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using MimeKit;
using TaskManagement.Infrastructure.Email;

namespace TaskManagement.IntegrationTests.Infrastructure;

/// <summary>Captures sent emails instead of delivering them.</summary>
public sealed partial class FakeEmailTransport : IEmailTransport
{
    private readonly ConcurrentQueue<MimeMessage> _sent = new();
    private readonly ConcurrentDictionary<string, bool> _failNextFor = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<MimeMessage> Sent => _sent.ToList();

    /// <summary>Makes the next send to <paramref name="email"/> fail (per recipient, so parallel tests do not interfere).</summary>
    public void FailNextFor(string email) => _failNextFor[email] = true;

    public Task SendAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        foreach (var recipient in message.To.Mailboxes)
        {
            if (_failNextFor.TryRemove(recipient.Address, out _))
            {
                throw new InvalidOperationException("Simulated SMTP failure");
            }
        }

        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<MimeMessage> To(string email) =>
        Sent.Where(m => m.To.Mailboxes.Any(a => string.Equals(a.Address, email, StringComparison.OrdinalIgnoreCase))).ToList();

    /// <summary>Extracts the token from the most recent password link sent to <paramref name="email"/>.</summary>
    public string LatestTokenFor(string email)
    {
        var message = To(email).LastOrDefault() ?? throw new InvalidOperationException($"No email was sent to {email}.");
        var match = TokenPattern().Match(message.TextBody ?? string.Empty);
        return match.Success ? Uri.UnescapeDataString(match.Groups[1].Value) : throw new InvalidOperationException("No token link in the email.");
    }

    [GeneratedRegex(@"\?token=([A-Za-z0-9_%\-]+)")]
    private static partial Regex TokenPattern();
}
