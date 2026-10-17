using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MimeKit;

namespace TaskManagement.Infrastructure.Email;

/// <summary>Delivers one already-built message. Replaced by an in-memory fake in integration tests.</summary>
public interface IEmailTransport
{
    Task SendAsync(MimeMessage message, CancellationToken cancellationToken);
}

internal sealed class SmtpEmailTransport(IOptions<EmailOptions> options) : IEmailTransport
{
    public async Task SendAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var security = Enum.Parse<SecureSocketOptions>(settings.SmtpSecurity, ignoreCase: true);
        using var client = new SmtpClient();
        await client.ConnectAsync(settings.SmtpHost, settings.SmtpPort, security, cancellationToken);
        if (!string.IsNullOrEmpty(settings.SmtpUserName))
        {
            await client.AuthenticateAsync(settings.SmtpUserName, settings.SmtpPassword ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}

internal sealed class PickupDirectoryEmailTransport(IOptions<EmailOptions> options, IHostEnvironment environment) : IEmailTransport
{
    public async Task SendAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        var directory = Path.GetFullPath(options.Value.PickupDirectory, environment.ContentRootPath);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.eml");
        await message.WriteToAsync(path, cancellationToken);
    }
}
