using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using TaskManagement.Application.Attachments;
using TaskManagement.Infrastructure.Files;

namespace TaskManagement.IntegrationTests.Platform;

/// <summary>The clamd INSTREAM protocol, against a small fake clamd that records what it was sent.</summary>
public sealed class ClamAvScannerTests
{
    private static async Task<(string Reply, byte[] Received, string Command)> RunAsync(string reply, Func<ClamAvScanner, Task> act)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = new MemoryStream();
        var command = string.Empty;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            var name = new List<byte>();
            var one = new byte[1];
            while (await stream.ReadAsync(one) == 1 && one[0] != 0)
            {
                name.Add(one[0]);
            }

            command = Encoding.ASCII.GetString([.. name]);
            if (command == "zINSTREAM")
            {
                var header = new byte[4];
                while (true)
                {
                    await stream.ReadExactlyAsync(header);
                    var length = BinaryPrimitives.ReadUInt32BigEndian(header);
                    if (length == 0)
                    {
                        break;
                    }

                    var chunk = new byte[length];
                    await stream.ReadExactlyAsync(chunk);
                    received.Write(chunk);
                }
            }

            await stream.WriteAsync(Encoding.ASCII.GetBytes(reply + "\0"));
        });

        var scanner = new ClamAvScanner(Options.Create(new AttachmentOptions { ClamAvHost = "127.0.0.1", ClamAvPort = port, ClamAvTimeout = TimeSpan.FromSeconds(10) }));
        await act(scanner);
        await server;
        return (reply, received.ToArray(), command);
    }

    [Fact]
    public async Task Clean_files_are_streamed_in_chunks()
    {
        var content = new byte[200_000];
        Random.Shared.NextBytes(content);
        MalwareScanVerdict verdict = default;
        var (_, received, command) = await RunAsync("stream: OK", async s => verdict = await s.ScanAsync(new MemoryStream(content), CancellationToken.None));
        Assert.Equal("zINSTREAM", command);
        Assert.Equal(MalwareScanVerdict.Clean, verdict);
        Assert.Equal(content, received);
    }

    [Fact]
    public async Task A_signature_match_is_infected()
    {
        MalwareScanVerdict verdict = default;
        await RunAsync("stream: Eicar-Test-Signature FOUND", async s => verdict = await s.ScanAsync(new MemoryStream([1, 2, 3]), CancellationToken.None));
        Assert.Equal(MalwareScanVerdict.Infected, verdict);
    }

    [Fact]
    public async Task Any_other_answer_is_a_failure_never_clean() =>
        await RunAsync("INSTREAM size limit exceeded. ERROR", async s =>
            await Assert.ThrowsAsync<InvalidOperationException>(() => s.ScanAsync(new MemoryStream([1]), CancellationToken.None)));

    [Fact]
    public async Task Ping_checks_that_clamd_answers()
    {
        var ok = false;
        var (_, _, command) = await RunAsync("PONG", async s => ok = await s.PingAsync(CancellationToken.None));
        Assert.Equal("zPING", command);
        Assert.True(ok);
    }
}
