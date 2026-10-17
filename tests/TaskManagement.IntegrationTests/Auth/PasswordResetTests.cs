using System.Diagnostics;
using System.Net;
using Microsoft.EntityFrameworkCore;
using TaskManagement.Domain.Access;
using TaskManagement.IntegrationTests.Infrastructure;

namespace TaskManagement.IntegrationTests.Auth;

[Collection(ApiCollection.Name)]
public sealed class PasswordResetTests(ApiFactory factory)
{
    private async Task<string> RequestResetAsync(TestClient client, string email)
    {
        var response = await client.PostAsync("/api/v1/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await factory.FlushEmailsAsync();
        return factory.Emails.LatestTokenFor(email);
    }

    [Fact]
    public async Task AC_AUTH_4_unknown_email_gets_the_same_response_and_no_email()
    {
        using var client = factory.CreateTestClient();
        await client.RefreshAntiforgeryAsync();
        var unknown = $"nobody-{Guid.NewGuid():N}@example.com";
        var known = await factory.CreateMemberAsync(SystemRoles.DeveloperId);

        var stopwatch = Stopwatch.StartNew();
        var unknownResponse = await client.PostAsync("/api/v1/auth/forgot-password", new { email = unknown });
        var unknownElapsed = stopwatch.Elapsed;
        var knownResponse = await client.PostAsync("/api/v1/auth/forgot-password", new { email = known.Email });
        await factory.FlushEmailsAsync();

        Assert.Equal(HttpStatusCode.Accepted, unknownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, knownResponse.StatusCode);
        Assert.Equal(await unknownResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            await knownResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(factory.Emails.To(unknown));
        Assert.Single(factory.Emails.To(known.Email));
        Assert.True(unknownElapsed >= TimeSpan.FromMilliseconds(390), "Responses are padded so timing does not reveal accounts.");
    }

    [Fact]
    public async Task AC_AUTH_5_reset_token_works_once_and_signs_out_existing_sessions()
    {
        var member = await factory.CreateMemberAsync(SystemRoles.DeveloperId);
        using var existingSession = factory.CreateTestClient();
        await existingSession.SignInAsync(member.Email, member.Password);
        using var client = factory.CreateTestClient();
        await client.RefreshAntiforgeryAsync();
        var token = await RequestResetAsync(client, member.Email);

        var first = await client.PostAsync("/api/v1/auth/reset-password", new { token, newPassword = "amber-glacier-violin-31" });
        var second = await client.PostAsync("/api/v1/auth/reset-password", new { token, newPassword = "amber-glacier-violin-32" });

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal("auth.invalid_token", await second.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await existingSession.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SignInAsync(member.Email, "amber-glacier-violin-31", ensureSuccess: false)).StatusCode);
    }

    [Fact]
    public async Task AC_AUTH_5_expired_token_is_rejected()
    {
        var member = await factory.CreateMemberAsync(SystemRoles.DeveloperId);
        using var client = factory.CreateTestClient();
        await client.RefreshAntiforgeryAsync();
        var token = await RequestResetAsync(client, member.Email);
        await factory.WithDbAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE identity.password_tokens SET expires_at = now() - interval '1 minute' WHERE user_id = {member.Id}"));

        var response = await client.PostAsync("/api/v1/auth/reset-password", new { token, newPassword = "amber-glacier-violin-31" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("auth.invalid_token", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Requesting_a_new_link_invalidates_the_older_one()
    {
        var member = await factory.CreateMemberAsync(SystemRoles.DeveloperId);
        using var client = factory.CreateTestClient();
        await client.RefreshAntiforgeryAsync();
        var older = await RequestResetAsync(client, member.Email);
        var newer = await RequestResetAsync(client, member.Email);

        var olderResponse = await client.PostAsync("/api/v1/auth/reset-password", new { token = older, newPassword = "amber-glacier-violin-31" });
        var newerResponse = await client.PostAsync("/api/v1/auth/reset-password", new { token = newer, newPassword = "amber-glacier-violin-31" });

        Assert.Equal(HttpStatusCode.BadRequest, olderResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, newerResponse.StatusCode);
    }

    [Fact]
    public async Task Weak_new_password_is_rejected_and_the_token_stays_usable()
    {
        var member = await factory.CreateMemberAsync(SystemRoles.DeveloperId);
        using var client = factory.CreateTestClient();
        await client.RefreshAntiforgeryAsync();
        var token = await RequestResetAsync(client, member.Email);

        var weak = await client.PostAsync("/api/v1/auth/reset-password", new { token, newPassword = "short" });
        var good = await client.PostAsync("/api/v1/auth/reset-password", new { token, newPassword = "amber-glacier-violin-31" });

        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Equal("validation_failed", await weak.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.NoContent, good.StatusCode);
    }

    [Fact]
    public async Task Garbage_token_is_rejected()
    {
        using var client = factory.CreateTestClient();
        await client.RefreshAntiforgeryAsync();

        var response = await client.PostAsync("/api/v1/auth/reset-password", new { token = "not-a-real-token", newPassword = "amber-glacier-violin-31" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("auth.invalid_token", await response.ProblemCodeAsync());
    }
}
