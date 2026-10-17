using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaskManagement.IntegrationTests.Infrastructure;

namespace TaskManagement.IntegrationTests.Api;

/// <summary>AC-ERR-1: unexpected errors return Problem Details with a trace ID and no internal details.</summary>
public sealed class UnhandledExceptionTests : IAsyncLifetime
{
    private const string SecretLookingMessage = "Internal failure: Host=db-prod;Password=hunter2";

    private readonly ApiFactory _factory = new()
    {
        // The system-info endpoint reads the clock; a broken clock makes it throw.
        ConfigureServices = services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new ThrowingTimeProvider())),
    };

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Unhandled_exception_returns_generic_problem_details()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/system/info", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("hunter2", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ThrowingTimeProvider", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);

        var problem = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.Equal("internal_error", problem.GetProperty("code").GetString());
        Assert.Equal("An unexpected error occurred.", problem.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.False(problem.TryGetProperty("detail", out _));
    }

    private sealed class ThrowingTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException(SecretLookingMessage);
    }
}
