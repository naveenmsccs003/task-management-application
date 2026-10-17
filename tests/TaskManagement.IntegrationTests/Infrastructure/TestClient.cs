using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TaskManagement.IntegrationTests.Infrastructure;

/// <summary>
/// HTTP client that behaves like the Angular app: fetches an antiforgery token and sends it as
/// <c>X-XSRF-TOKEN</c> on state-changing requests, refreshing it after sign-in and sign-out.
/// </summary>
public sealed class TestClient(HttpClient http) : IDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public HttpClient Http { get; } = http;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public async Task RefreshAntiforgeryAsync()
    {
        var response = await Http.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/auth/antiforgery", UriKind.Relative), Cancel);
        Http.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Http.DefaultRequestHeaders.Add("X-XSRF-TOKEN", response.GetProperty("requestToken").GetString());
    }

    public async Task<HttpResponseMessage> SignInAsync(string email, string password, bool ensureSuccess = true)
    {
        await RefreshAntiforgeryAsync();
        var response = await PostAsync("/api/v1/auth/login", new { email, password });
        if (ensureSuccess)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await RefreshAntiforgeryAsync();
        return response;
    }

    public Task<HttpResponseMessage> GetAsync(string path) => Http.GetAsync(new Uri(path, UriKind.Relative), Cancel);

    public async Task<T> GetJsonAsync<T>(string path)
    {
        var response = await GetAsync(path);
        Assert.True(response.IsSuccessStatusCode, $"GET {path} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Cancel)}");
        return (await response.Content.ReadFromJsonAsync<T>(Json, Cancel))!;
    }

    public Task<HttpResponseMessage> PostAsync(string path, object? body = null) =>
        Http.PostAsJsonAsync(new Uri(path, UriKind.Relative), body ?? new { }, Json, Cancel);

    public Task<HttpResponseMessage> PatchAsync(string path, object body) =>
        Http.PatchAsJsonAsync(new Uri(path, UriKind.Relative), body, Json, Cancel);

    public Task<HttpResponseMessage> PutAsync(string path, object body) =>
        Http.PutAsJsonAsync(new Uri(path, UriKind.Relative), body, Json, Cancel);

    public Task<HttpResponseMessage> DeleteAsync(string path) => Http.DeleteAsync(new Uri(path, UriKind.Relative), Cancel);

    public void Dispose() => Http.Dispose();
}

public static class ResponseExtensions
{
    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(TestClient.Json, TestContext.Current.CancellationToken);

    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response)
    {
        var json = await response.JsonAsync();
        return json.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
