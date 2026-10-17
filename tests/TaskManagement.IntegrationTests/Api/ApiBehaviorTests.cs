using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TaskManagement.IntegrationTests.Infrastructure;

namespace TaskManagement.IntegrationTests.Api;

[Collection(ApiCollection.Name)]
public sealed class ApiBehaviorTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Liveness_endpoint_reports_healthy()
    {
        var response = await _client.GetAsync(new Uri("/health/live", UriKind.Relative), Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(Cancel));
    }

    [Fact]
    public async Task Readiness_endpoint_checks_the_database()
    {
        var response = await _client.GetAsync(new Uri("/health/ready", UriKind.Relative), Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(Cancel));
    }

    [Fact]
    public async Task System_info_returns_name_version_and_server_time()
    {
        var before = DateTimeOffset.UtcNow;

        var info = await _client.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/system/info", UriKind.Relative), Cancel);

        Assert.Equal("Task Management", info.GetProperty("name").GetString());
        Assert.False(string.IsNullOrWhiteSpace(info.GetProperty("version").GetString()));
        var serverTime = info.GetProperty("serverTimeUtc").GetDateTimeOffset();
        Assert.InRange(serverTime, before.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal(TimeSpan.Zero, serverTime.Offset);
    }

    [Fact]
    public async Task Unknown_api_route_returns_problem_details_with_trace_id()
    {
        var response = await _client.GetAsync(new Uri("/api/v1/does-not-exist", UriKind.Relative), Cancel);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Responses_include_security_headers()
    {
        var response = await _client.GetAsync(new Uri("/api/v1/system/info", UriKind.Relative), Cancel);

        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Contains("frame-ancestors 'none'", Header(response, "Content-Security-Policy"), StringComparison.Ordinal);
        Assert.Equal("strict-origin-when-cross-origin", Header(response, "Referrer-Policy"));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task OpenApi_document_is_available_in_testing_and_lists_endpoints()
    {
        var document = await _client.GetFromJsonAsync<JsonElement>(new Uri("/openapi/v1.json", UriKind.Relative), Cancel);

        Assert.True(document.GetProperty("paths").TryGetProperty("/api/v1/system/info", out _));
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : string.Empty;
}
