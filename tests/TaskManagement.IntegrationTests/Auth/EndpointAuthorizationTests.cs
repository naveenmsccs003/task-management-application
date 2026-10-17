using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TaskManagement.IntegrationTests.Infrastructure;

namespace TaskManagement.IntegrationTests.Auth;

/// <summary>Guards against an endpoint shipping without an authorization decision (FR-ROLE-05, NFR-SEC-09).</summary>
[Collection(ApiCollection.Name)]
public sealed class EndpointAuthorizationTests(ApiFactory factory)
{
    private IEnumerable<RouteEndpoint> ApiEndpoints() =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true);

    [Fact]
    public void Every_api_endpoint_requires_authorization_or_is_explicitly_anonymous()
    {
        var undecided = ApiEndpoints()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null && e.Metadata.GetMetadata<IAuthorizeData>() is null)
            .Select(e => e.DisplayName)
            .ToList();

        Assert.True(undecided.Count == 0, "Endpoints without an authorization decision:\n" + string.Join("\n", undecided));
    }

    [Fact]
    public async Task Every_protected_endpoint_returns_401_to_anonymous_callers()
    {
        using var client = factory.CreateTestClient();
        await client.RefreshAntiforgeryAsync();
        var failures = new List<string>();

        foreach (var endpoint in ApiEndpoints().Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null))
        {
            var path = "/" + string.Join('/', endpoint.RoutePattern.PathSegments.Select(segment =>
                string.Concat(segment.Parts.Select(part => part switch
                {
                    Microsoft.AspNetCore.Routing.Patterns.RoutePatternLiteralPart literal => literal.Content,
                    Microsoft.AspNetCore.Routing.Patterns.RoutePatternParameterPart => Guid.NewGuid().ToString(),
                    _ => string.Empty,
                }))));
            foreach (var method in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), path.TrimEnd('/'));
                if (method is not "GET")
                {
                    request.Content = System.Net.Http.Json.JsonContent.Create(new { });
                }

                var response = await client.Http.SendAsync(request, TestContext.Current.CancellationToken);
                if (response.StatusCode != HttpStatusCode.Unauthorized)
                {
                    failures.Add($"{method} {path} -> {(int)response.StatusCode}");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
