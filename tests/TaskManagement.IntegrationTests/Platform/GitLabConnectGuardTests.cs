using System.Net;
using System.Net.Sockets;
using TaskManagement.Application.GitLab;
using TaskManagement.Infrastructure.GitLab;

namespace TaskManagement.IntegrationTests.Platform;

/// <summary>security.md S19: the GitLab client checks the resolved address when it connects, not only when a URL is saved.</summary>
public sealed class GitLabConnectGuardTests
{
    private static HttpClient Client(GitLabOptions options) =>
        new(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectCallback = (context, ct) => GitLabHttpClient.ConnectAsync(context, options, ct),
        });

    [Fact]
    public async Task Loopback_is_refused_unless_the_host_is_allow_listed()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            await using var stream = client.GetStream();
            var buffer = new byte[4096];
            _ = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);
            await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray(), TestContext.Current.CancellationToken);
        }, TestContext.Current.CancellationToken);

        using var strict = Client(new GitLabOptions());
        var refused = await Assert.ThrowsAsync<HttpRequestException>(() => strict.GetAsync(new Uri($"http://localhost:{port}/"), TestContext.Current.CancellationToken));
        Assert.Contains("not allowed", refused.Message, StringComparison.Ordinal);

        var options = new GitLabOptions();
        options.AllowedInsecureHosts.Add("localhost");
        using var relaxed = Client(options);
        Assert.Equal("ok", await relaxed.GetStringAsync(new Uri($"http://localhost:{port}/"), TestContext.Current.CancellationToken));
        await server;
    }

    [Fact]
    public async Task Metadata_addresses_are_never_reached()
    {
        var options = new GitLabOptions();
        options.AllowedInsecureHosts.Add("169.254.169.254");
        using var client = Client(options);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(new Uri("http://169.254.169.254/latest/meta-data/"), TestContext.Current.CancellationToken));
    }
}
