using System.Net;
using Microsoft.EntityFrameworkCore;
using TaskManagement.Domain.Access;
using TaskManagement.IntegrationTests.Infrastructure;

namespace TaskManagement.IntegrationTests.Auth;

[Collection(ApiCollection.Name)]
public sealed class AuthenticationTests(ApiFactory factory)
{
    [Theory]
    [InlineData("/api/v1/auth/register")]
    [InlineData("/api/v1/auth/signup")]
    [InlineData("/api/v1/register")]
    public async Task AC_AUTH_1_there_is_no_public_registration_endpoint(string path)
    {
        using var client = factory.CreateTestClient();
        await client.RefreshAntiforgeryAsync();

        var response = await client.PostAsync(path, new { email = "new@example.com", password = ApiFactory.DefaultPassword });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AC_AUTH_2_sign_in_sets_a_secure_http_only_cookie_and_me_returns_permissions()
    {
        var member = await factory.CreateMemberAsync(SystemRoles.DeveloperId);
        using var client = factory.CreateTestClient();

        var response = await client.SignInAsync(member.Email, member.Password);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-tma=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);

        var me = await (await client.GetAsync("/api/v1/me")).JsonAsync();
        Assert.Equal(member.Email, me.GetProperty("email").GetString());
        Assert.Equal("Developer", me.GetProperty("globalRole").GetProperty("name").GetString());
        Assert.Contains(me.GetProperty("permissions").EnumerateArray(), p => p.GetProperty("code").GetString() == PermissionCatalog.ProfileManage);
    }

    [Fact]
    public async Task Unknown_email_wrong_password_inactive_and_invited_accounts_get_identical_responses()
    {
        var active = await factory.CreateMemberAsync(SystemRoles.DeveloperId);
        var invited = await factory.CreateMemberAsync(SystemRoles.DeveloperId, password: null);
        var inactive = await factory.CreateMemberAsync(SystemRoles.DeveloperId);
        await factory.WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == inactive.Id);
            user.Deactivate(Guid.NewGuid(), DateTimeOffset.UtcNow);
            return await db.SaveChangesAsync();
        });

        var bodies = new List<string>();
        foreach (var (email, password) in new[]
                 {
                     ("nobody-" + Guid.NewGuid() + "@example.com", ApiFactory.DefaultPassword),
                     (active.Email, "wrong-password-123"),
                     (invited.Email, ApiFactory.DefaultPassword),
                     (inactive.Email, ApiFactory.DefaultPassword),
                 })
        {
            using var client = factory.CreateTestClient();
            var response = await client.SignInAsync(email, password, ensureSuccess: false);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var json = await response.JsonAsync();
            bodies.Add($"{json.GetProperty("code").GetString()}|{json.GetProperty("title").GetString()}");
        }

        Assert.Single(bodies.Distinct());
        Assert.StartsWith("auth.invalid_credentials|", bodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AC_AUTH_3_account_locks_after_five_failures_even_with_the_correct_password()
    {
        var member = await factory.CreateMemberAsync(SystemRoles.DeveloperId);
        using var client = factory.CreateTestClient();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SignInAsync(member.Email, "wrong-password-123", ensureSuccess: false)).StatusCode);
        }

        var response = await client.SignInAsync(member.Email, member.Password, ensureSuccess: false);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var audited = await factory.WithDbAsync(db =>
            db.AuditEntries.AnyAsync(e => e.Action == "auth.locked_out" && e.EntityId == member.Id.ToString()));
        Assert.True(audited);
    }

    [Fact]
    public async Task Parallel_wrong_guesses_cannot_bypass_the_lockout_counter()
    {
        var member = await factory.CreateMemberAsync(SystemRoles.DeveloperId);
        using var client = factory.CreateTestClient();
        await client.RefreshAntiforgeryAsync();

        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ =>
            client.PostAsync("/api/v1/auth/login", new { email = member.Email, password = "wrong-password-123" })));

        var lockedUntil = await factory.WithDbAsync(db => db.Users.Where(u => u.Id == member.Id).Select(u => u.LockoutEndsAt).SingleAsync());
        Assert.NotNull(lockedUntil);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SignInAsync(member.Email, member.Password, ensureSuccess: false)).StatusCode);
    }

    [Fact]
    public async Task State_changing_requests_without_an_antiforgery_token_are_rejected()
    {
        var member = await factory.CreateMemberAsync(SystemRoles.DeveloperId);
        using var client = factory.CreateTestClient();

        var response = await client.PostAsync("/api/v1/auth/login", new { email = member.Email, password = member.Password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("antiforgery_invalid", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Requests_without_a_session_get_401_problem_details()
    {
        using var client = factory.CreateTestClient();

        var response = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("auth.required", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Sign_out_ends_the_session()
    {
        using var client = await factory.SignedInClientAsync(SystemRoles.DeveloperId);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/logout")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Deactivated_member_is_signed_out_on_the_next_request()
    {
        var member = await factory.CreateMemberAsync(SystemRoles.DeveloperId);
        using var memberClient = factory.CreateTestClient();
        await memberClient.SignInAsync(member.Email, member.Password);
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/v1/members/{member.Id}/deactivate")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await memberClient.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Changing_password_keeps_this_session_and_ends_other_sessions()
    {
        var member = await factory.CreateMemberAsync(SystemRoles.DeveloperId);
        using var current = factory.CreateTestClient();
        using var other = factory.CreateTestClient();
        await current.SignInAsync(member.Email, member.Password);
        await other.SignInAsync(member.Email, member.Password);

        var response = await current.PostAsync("/api/v1/auth/change-password", new { currentPassword = member.Password, newPassword = "violet-harbor-engine-77" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await current.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Change_password_requires_the_current_password_and_the_policy()
    {
        var member = await factory.CreateMemberAsync(SystemRoles.DeveloperId);
        using var client = factory.CreateTestClient();
        await client.SignInAsync(member.Email, member.Password);

        var wrongCurrent = await client.PostAsync("/api/v1/auth/change-password", new { currentPassword = "nope-nope-nope-1", newPassword = "violet-harbor-engine-77" });
        var weak = await client.PostAsync("/api/v1/auth/change-password", new { currentPassword = member.Password, newPassword = "Password1234" });

        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrent.StatusCode);
        Assert.True((await wrongCurrent.JsonAsync()).GetProperty("errors").TryGetProperty("currentPassword", out _));
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.True((await weak.JsonAsync()).GetProperty("errors").TryGetProperty("newPassword", out _));
    }

    [Fact]
    public async Task AC_AUTH_6_passwords_and_tokens_are_never_stored_in_plain_text()
    {
        var member = await factory.CreateMemberAsync(SystemRoles.DeveloperId);
        using var client = factory.CreateTestClient();
        await client.RefreshAntiforgeryAsync();
        await client.PostAsync("/api/v1/auth/forgot-password", new { email = member.Email });
        await factory.FlushEmailsAsync();
        var token = factory.Emails.LatestTokenFor(member.Email);

        var leaks = await factory.WithDbAsync(async db =>
        {
            var plain = new List<string>();
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == member.Id);
            if (user.PasswordHash!.Contains(member.Password, StringComparison.Ordinal))
            {
                plain.Add("password");
            }

            var tokenHashes = await db.PasswordTokens.AsNoTracking().Where(t => t.UserId == member.Id).Select(t => t.TokenHash).ToListAsync();
            if (tokenHashes.Any(h => System.Text.Encoding.UTF8.GetString(h).Contains(token, StringComparison.Ordinal)))
            {
                plain.Add("token");
            }

            var emailBodies = await db.EmailDeliveries.AsNoTracking().Where(e => e.RecipientEmail == member.Email).Select(e => e.BodyText + e.BodyHtml).ToListAsync();
            if (emailBodies.Any(b => b.Contains(token, StringComparison.Ordinal)))
            {
                plain.Add("email body");
            }

            var auditText = await db.AuditEntries.AsNoTracking().Where(e => e.EntityId == member.Id.ToString()).Select(e => e.ChangesJson).ToListAsync();
            if (auditText.Any(t => t != null && (t.Contains(token, StringComparison.Ordinal) || t.Contains(member.Password, StringComparison.Ordinal))))
            {
                plain.Add("audit");
            }

            return plain;
        });

        Assert.Empty(leaks);
    }
}
