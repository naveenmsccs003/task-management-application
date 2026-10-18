using System.Net;
using TaskManagement.Application.GitLab;

namespace TaskManagement.Application.Tests.GitLab;

public sealed class GitLabUrlsTests
{
    private static readonly GitLabOptions Options = new();
    private static readonly GitLabOptions DevOptions = CreateDev();

    private static GitLabOptions CreateDev()
    {
        var options = new GitLabOptions();
        options.AllowedInsecureHosts.Add("localhost");
        return options;
    }

    [Theory]
    [InlineData("https://gitlab.com", "https://gitlab.com")]
    [InlineData(" https://gitlab.example.com/ ", "https://gitlab.example.com")]
    [InlineData("https://example.com/gitlab/", "https://example.com/gitlab")]
    [InlineData("https://10.0.0.5:8443", "https://10.0.0.5:8443")] // self-managed GitLab on a private network is fine
    public void Valid_https_addresses_are_normalized(string input, string expected)
    {
        Assert.Equal(expected, GitLabUrls.Normalize(input, Options, out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData("http://gitlab.example.com")]
    [InlineData("https://169.254.169.254")]
    [InlineData("https://[fe80::1]")]
    [InlineData("https://127.0.0.1")]
    [InlineData("https://0.0.0.0")]
    [InlineData("https://224.0.0.1")]
    [InlineData("https://a:b@gitlab.example.com")]
    [InlineData("https://gitlab.example.com/?x=1")]
    [InlineData("https://gitlab.example.com/#top")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gitlab.example.com")]
    [InlineData("")]
    [InlineData(null)]
    public void Unsafe_or_malformed_addresses_are_refused(string? input)
    {
        Assert.Null(GitLabUrls.Normalize(input, Options, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Allow_listed_hosts_may_use_http_and_loopback()
    {
        Assert.Equal("http://localhost:8929", GitLabUrls.Normalize("http://localhost:8929", DevOptions, out _));
        Assert.True(GitLabUrls.IsBlocked(IPAddress.Loopback, allowListed: false));
        Assert.False(GitLabUrls.IsBlocked(IPAddress.Loopback, allowListed: true));
        Assert.True(GitLabUrls.IsBlocked(IPAddress.Parse("169.254.169.254"), allowListed: true)); // never, even allow-listed
        Assert.True(GitLabUrls.IsBlocked(IPAddress.Parse("::ffff:169.254.169.254"), allowListed: false));
        Assert.False(GitLabUrls.IsBlocked(IPAddress.Parse("192.168.1.20"), allowListed: false));
    }
}
