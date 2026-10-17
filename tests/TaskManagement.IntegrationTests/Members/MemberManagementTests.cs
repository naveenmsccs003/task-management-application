using System.Net;
using Microsoft.EntityFrameworkCore;
using TaskManagement.Domain.Access;
using TaskManagement.IntegrationTests.Infrastructure;

namespace TaskManagement.IntegrationTests.Members;

[Collection(ApiCollection.Name)]
public sealed class MemberManagementTests(ApiFactory factory)
{
    private static object NewMember(string? email = null, Guid? roleId = null) => new
    {
        email = email ?? $"new-{Guid.NewGuid():N}@example.com",
        fullName = "Priya Sharma",
        globalRoleId = roleId ?? SystemRoles.DeveloperId,
        timeZone = "Asia/Kolkata",
        employeeCode = (string?)null,
    };

    [Theory]
    [InlineData("Developer")]
    [InlineData("Team Leader")]
    [InlineData("Project Manager")]
    [InlineData("Viewer")]
    public async Task AC_MEM_1_only_admins_can_create_accounts(string roleName)
    {
        var roleId = SystemRoles.All.Single(r => r.Name == roleName).Id;
        using var client = await factory.SignedInClientAsync(roleId);
        var email = $"blocked-{Guid.NewGuid():N}@example.com";

        var response = await client.PostAsync("/api/v1/members", NewMember(email));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(await factory.WithDbAsync(db => db.Users.AnyAsync(u => u.Email == email)));
    }

    [Fact]
    public async Task Admin_creates_member_who_sets_a_password_from_the_invite_and_signs_in()
    {
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);
        var email = $"invitee-{Guid.NewGuid():N}@example.com";

        var created = await admin.PostAsync("/api/v1/members", NewMember(email));
        await factory.FlushEmailsAsync();

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var member = await created.JsonAsync();
        Assert.False(member.GetProperty("hasPassword").GetBoolean());
        var invite = Assert.Single(factory.Emails.To(email));
        Assert.Contains("https://tma.test/set-password?token=", invite.TextBody, StringComparison.Ordinal);

        using var invitee = factory.CreateTestClient();
        await invitee.RefreshAntiforgeryAsync();
        var set = await invitee.PostAsync("/api/v1/auth/reset-password", new { token = factory.Emails.LatestTokenFor(email), newPassword = "maple-orbit-canyon-58" });
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);
        await invitee.SignInAsync(email, "maple-orbit-canyon-58");
        Assert.Equal(HttpStatusCode.OK, (await invitee.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Duplicate_email_is_rejected_case_insensitively()
    {
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);
        var email = $"dup-{Guid.NewGuid():N}@example.com";
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsync("/api/v1/members", NewMember(email))).StatusCode);

        var response = await admin.PostAsync("/api/v1/members", NewMember(email.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("member.email_taken", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Invalid_input_returns_field_errors()
    {
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);

        var response = await admin.PostAsync("/api/v1/members", new
        {
            email = "not-an-email",
            fullName = "",
            globalRoleId = Guid.NewGuid(),
            timeZone = "Nowhere/Land",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.JsonAsync()).GetProperty("errors");
        foreach (var field in new[] { "email", "fullName", "globalRoleId", "timeZone" })
        {
            Assert.True(errors.TryGetProperty(field, out _), $"Expected an error for {field}.");
        }
    }

    [Fact]
    public async Task Stale_version_is_rejected_instead_of_overwriting()
    {
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);
        var member = await factory.CreateMemberAsync(SystemRoles.DeveloperId);
        var loaded = await admin.GetJsonAsync<System.Text.Json.JsonElement>($"/api/v1/members/{member.Id}");
        var version = loaded.GetProperty("version").GetUInt32();
        object Update(string name) => new { fullName = name, timeZone = "UTC", employeeCode = (string?)null, globalRoleId = SystemRoles.DeveloperId, version };

        var first = await admin.PatchAsync($"/api/v1/members/{member.Id}", Update("First Edit"));
        var second = await admin.PatchAsync($"/api/v1/members/{member.Id}", Update("Second Edit"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("concurrency_conflict", await second.ProblemCodeAsync());
        Assert.Equal("First Edit", (await admin.GetJsonAsync<System.Text.Json.JsonElement>($"/api/v1/members/{member.Id}")).GetProperty("fullName").GetString());
    }

    [Fact]
    public async Task Admin_cannot_change_own_role_or_deactivate_self()
    {
        var adminMember = await factory.CreateMemberAsync(SystemRoles.AdminId);
        using var admin = factory.CreateTestClient();
        await admin.SignInAsync(adminMember.Email, adminMember.Password);
        var me = await admin.GetJsonAsync<System.Text.Json.JsonElement>($"/api/v1/members/{adminMember.Id}");

        var demote = await admin.PatchAsync($"/api/v1/members/{adminMember.Id}", new
        {
            fullName = "Me",
            timeZone = "UTC",
            employeeCode = (string?)null,
            globalRoleId = SystemRoles.ViewerId,
            version = me.GetProperty("version").GetUInt32(),
        });
        var deactivate = await admin.PostAsync($"/api/v1/members/{adminMember.Id}/deactivate");

        Assert.Equal("member.self_change", await demote.ProblemCodeAsync());
        Assert.Equal("member.self_change", await deactivate.ProblemCodeAsync());
    }

    [Fact]
    public async Task Role_change_takes_effect_immediately_and_ends_the_members_sessions()
    {
        var member = await factory.CreateMemberAsync(SystemRoles.DeveloperId);
        using var memberClient = factory.CreateTestClient();
        await memberClient.SignInAsync(member.Email, member.Password);
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.GetAsync("/api/v1/members")).StatusCode);

        var loaded = await admin.GetJsonAsync<System.Text.Json.JsonElement>($"/api/v1/members/{member.Id}");
        var update = await admin.PatchAsync($"/api/v1/members/{member.Id}", new
        {
            fullName = "Promoted",
            timeZone = "UTC",
            employeeCode = (string?)null,
            globalRoleId = SystemRoles.AdminId,
            version = loaded.GetProperty("version").GetUInt32(),
        });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await memberClient.GetAsync("/api/v1/members")).StatusCode);
        await memberClient.SignInAsync(member.Email, member.Password);
        Assert.Equal(HttpStatusCode.OK, (await memberClient.GetAsync("/api/v1/members")).StatusCode);
    }

    [Fact]
    public async Task Member_list_is_searchable_and_paginated()
    {
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);
        var tag = Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < 3; i++)
        {
            await admin.PostAsync("/api/v1/members", NewMember($"search-{tag}-{i}@example.com"));
        }

        var page = await admin.GetJsonAsync<System.Text.Json.JsonElement>($"/api/v1/members?search={tag}&page=1&pageSize=2");

        Assert.Equal(3, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, page.GetProperty("items").GetArrayLength());
        Assert.Equal(2, page.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task Admin_password_reset_emails_the_member_and_ends_their_sessions()
    {
        var member = await factory.CreateMemberAsync(SystemRoles.DeveloperId);
        using var memberClient = factory.CreateTestClient();
        await memberClient.SignInAsync(member.Email, member.Password);
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);

        var response = await admin.PostAsync($"/api/v1/members/{member.Id}/send-password-reset");
        await factory.FlushEmailsAsync();

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("/reset-password?token=", Assert.Single(factory.Emails.To(member.Email)).TextBody, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, (await memberClient.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Unknown_member_returns_404()
    {
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);

        var response = await admin.GetAsync($"/api/v1/members/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("member.not_found", await response.ProblemCodeAsync());
    }
}
