using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TaskManagement.Domain.Access;
using TaskManagement.IntegrationTests.Infrastructure;

namespace TaskManagement.IntegrationTests.Administration;

[Collection(ApiCollection.Name)]
public sealed class AuditLogTests(ApiFactory factory)
{
    [Fact]
    public async Task AC_AUD_1_member_changes_are_audited_with_actor_and_old_and_new_values()
    {
        var adminMember = await factory.CreateMemberAsync(SystemRoles.AdminId);
        using var admin = factory.CreateTestClient();
        await admin.SignInAsync(adminMember.Email, adminMember.Password);
        var member = await factory.CreateMemberAsync(SystemRoles.DeveloperId);
        var loaded = await admin.GetJsonAsync<JsonElement>($"/api/v1/members/{member.Id}");

        await admin.PatchAsync($"/api/v1/members/{member.Id}", new
        {
            fullName = "Renamed Person",
            timeZone = "UTC",
            employeeCode = (string?)null,
            globalRoleId = SystemRoles.DeveloperId,
            version = loaded.GetProperty("version").GetUInt32(),
        });

        var page = await admin.GetJsonAsync<JsonElement>($"/api/v1/audit-logs?entityType=User&entityId={member.Id}&action=member.updated");
        var entry = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(adminMember.Id, entry.GetProperty("actorId").GetGuid());
        var change = entry.GetProperty("changes").GetProperty("fullName");
        Assert.Equal("Renamed Person", change.GetProperty("new").GetString());
        Assert.False(string.IsNullOrEmpty(entry.GetProperty("correlationId").GetString()));
    }

    [Fact]
    public async Task AC_AUD_2_audit_rows_cannot_be_updated_or_deleted()
    {
        var member = await factory.CreateMemberAsync(SystemRoles.DeveloperId);
        using var client = factory.CreateTestClient();
        await client.SignInAsync(member.Email, member.Password);

        var update = await Record.ExceptionAsync(() => factory.WithDbAsync(db =>
            db.Database.ExecuteSqlAsync($"UPDATE audit.audit_logs SET action = 'tampered' WHERE entity_id = {member.Id.ToString()}")));
        var delete = await Record.ExceptionAsync(() => factory.WithDbAsync(db =>
            db.Database.ExecuteSqlAsync($"DELETE FROM audit.audit_logs WHERE entity_id = {member.Id.ToString()}")));
        var truncate = await Record.ExceptionAsync(() => factory.WithDbAsync(db =>
            db.Database.ExecuteSqlRawAsync("TRUNCATE audit.audit_logs")));

        Assert.All(new[] { update, delete, truncate }, e => Assert.Contains("append-only", Assert.IsType<PostgresException>(e).MessageText, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Application_role_may_only_insert_and_select_audit_rows()
    {
        var privileges = await factory.WithDbAsync(db => db.Database.SqlQuery<string>($"""
            SELECT string_agg(p, ',' ORDER BY p) AS "Value" FROM unnest(ARRAY['SELECT','INSERT','UPDATE','DELETE','TRUNCATE']) AS p
            WHERE has_table_privilege('tma_app', 'audit.audit_logs', p)
            """).SingleAsync());

        Assert.Equal("INSERT,SELECT", privileges);
    }

    [Fact]
    public async Task Only_admins_can_search_the_audit_log()
    {
        using var pm = await factory.SignedInClientAsync(SystemRoles.ProjectManagerId);
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);

        Assert.Equal(HttpStatusCode.Forbidden, (await pm.GetAsync("/api/v1/audit-logs")).StatusCode);
        var page = await admin.GetJsonAsync<JsonElement>("/api/v1/audit-logs?action=auth.signed_in&pageSize=5");
        Assert.InRange(page.GetProperty("items").GetArrayLength(), 1, 5);
    }
}
