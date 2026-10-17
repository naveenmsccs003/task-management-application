using System.Net;
using TaskManagement.Domain.Access;
using TaskManagement.IntegrationTests.Infrastructure;

namespace TaskManagement.IntegrationTests.Administration;

/// <summary>Own database: changing global settings must not affect other test classes.</summary>
public sealed class SettingsTests : IAsyncLifetime
{
    private readonly ApiFactory _factory = new();

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static object Auth(int maxAttempts = 5, int minLength = 12) => new
    {
        passwordMinLength = minLength,
        maxFailedSignInAttempts = maxAttempts,
        lockoutMinutes = 15,
        resetTokenMinutes = 30,
        inviteTokenHours = 72,
    };

    [Fact]
    public async Task Changed_lockout_threshold_applies_without_restart()
    {
        using var admin = await _factory.SignedInClientAsync(SystemRoles.AdminId);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync("/api/v1/settings/auth", Auth(maxAttempts: 3))).StatusCode);
        var member = await _factory.CreateMemberAsync(SystemRoles.DeveloperId);
        using var client = _factory.CreateTestClient();

        for (var i = 0; i < 3; i++)
        {
            await client.SignInAsync(member.Email, "wrong-password-123", ensureSuccess: false);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SignInAsync(member.Email, member.Password, ensureSuccess: false)).StatusCode);
        var settings = await admin.GetJsonAsync<System.Text.Json.JsonElement>("/api/v1/settings");
        Assert.Equal(3, settings.GetProperty("auth").GetProperty("maxFailedSignInAttempts").GetInt32());
    }

    [Fact]
    public async Task Out_of_range_values_are_rejected()
    {
        using var admin = await _factory.SignedInClientAsync(SystemRoles.AdminId);

        var response = await admin.PutAsync("/api/v1/settings/auth", Auth(maxAttempts: 1, minLength: 4));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.JsonAsync()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("maxFailedSignInAttempts", out _));
        Assert.True(errors.TryGetProperty("passwordMinLength", out _));
    }

    [Fact]
    public async Task Only_admins_can_read_or_change_settings()
    {
        using var tl = await _factory.SignedInClientAsync(SystemRoles.TeamLeaderId);

        Assert.Equal(HttpStatusCode.Forbidden, (await tl.GetAsync("/api/v1/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tl.PutAsync("/api/v1/settings/auth", Auth())).StatusCode);
    }
}
