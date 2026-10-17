using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using TaskManagement.Application.Attachments;

namespace TaskManagement.Infrastructure.Files;

/// <summary>
/// Talks to ClamAV's clamd over TCP with the INSTREAM command (Q9): the file is sent in length-prefixed chunks and clamd
/// answers <c>stream: OK</c> or <c>stream: &lt;signature&gt; FOUND</c>. Any other answer (for example a size limit error)
/// is a failure to scan, never "clean". clamd's StreamMaxLength must be at least the largest allowed upload.
/// </summary>
public sealed class ClamAvScanner(IOptions<AttachmentOptions> options) : IMalwareScanner
{
    private const int ChunkSize = 64 * 1024;

    public bool IsEnabled => true;

    public async Task<MalwareScanVerdict> ScanAsync(Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var reply = await SendAsync("zINSTREAM\0"u8.ToArray(), content, cancellationToken);
        if (reply.EndsWith("OK", StringComparison.Ordinal))
        {
            return MalwareScanVerdict.Clean;
        }

        if (reply.EndsWith("FOUND", StringComparison.Ordinal))
        {
            return MalwareScanVerdict.Infected;
        }

        throw new InvalidOperationException($"ClamAV could not scan the file: {reply}");
    }

    /// <summary>For the readiness check: clamd answers PING with PONG.</summary>
    public async Task<bool> PingAsync(CancellationToken cancellationToken) =>
        await SendAsync("zPING\0"u8.ToArray(), null, cancellationToken) == "PONG";

    private async Task<string> SendAsync(byte[] command, Stream? content, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.ClamAvTimeout);
        using var client = new TcpClient();
        await client.ConnectAsync(settings.ClamAvHost, settings.ClamAvPort, timeout.Token);
        await using var stream = client.GetStream();
        await stream.WriteAsync(command, timeout.Token);
        if (content is not null)
        {
            var buffer = new byte[4 + ChunkSize];
            int read;
            while ((read = await content.ReadAsync(buffer.AsMemory(4, ChunkSize), timeout.Token)) > 0)
            {
                BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)read);
                await stream.WriteAsync(buffer.AsMemory(0, 4 + read), timeout.Token);
            }

            await stream.WriteAsync(new byte[4], timeout.Token); // zero-length chunk ends the stream
        }

        // The reply ends with a NUL byte (the "z" commands).
        var reply = new List<byte>();
        var one = new byte[1];
        while (await stream.ReadAsync(one, timeout.Token) == 1 && one[0] != 0)
        {
            reply.Add(one[0]);
            if (reply.Count > 4_096)
            {
                break;
            }
        }

        return Encoding.ASCII.GetString([.. reply]).Trim();
    }
}
