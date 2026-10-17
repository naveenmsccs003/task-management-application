using System.Net;
using TaskManagement.IntegrationTests.Infrastructure;

namespace TaskManagement.IntegrationTests.Auth;

/// <summary>NFR-SEC-05. Own host with low limits so other tests are unaffected.</summary>
public sealed class RateLimitTests : IAsyncLifetime
{
    private readonly ApiFactory _factory = new()
    {
        Settings = new Dictionary<string, string?>
        {
            ["Auth:SignInPermitsPerMinute"] = "3",
            ["Auth:PasswordPermitsPerQuarterHour"] = "2",
        },
    };

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Sign_in_is_rate_limited_per_client()
    {
        using var client = _factory.CreateTestClient();
        await client.RefreshAntiforgeryAsync();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            statuses.Add((await client.PostAsync("/api/v1/auth/login", new { email = "x@example.com", password = "wrong-password-123" })).StatusCode);
        }

        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact]
    public async Task Password_reset_requests_are_rate_limited()
    {
        using var client = _factory.CreateTestClient();
        await client.RefreshAntiforgeryAsync();

        await client.PostAsync("/api/v1/auth/forgot-password", new { email = "x@example.com" });
        await client.PostAsync("/api/v1/auth/forgot-password", new { email = "x@example.com" });
        var third = await client.PostAsync("/api/v1/auth/forgot-password", new { email = "x@example.com" });

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("rate_limited", await third.ProblemCodeAsync());
    }
}
