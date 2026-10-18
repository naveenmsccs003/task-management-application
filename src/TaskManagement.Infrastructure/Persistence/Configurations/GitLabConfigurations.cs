using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskManagement.Domain.GitLab;
using TaskManagement.Domain.Identity;
using TaskManagement.Domain.Projects;
using TaskManagement.Domain.Tasks;

namespace TaskManagement.Infrastructure.Persistence.Configurations;

internal sealed class GitLabConnectionConfiguration : IEntityTypeConfiguration<GitLabConnection>
{
    public void Configure(EntityTypeBuilder<GitLabConnection> builder)
    {
        builder.ToTable("connections", Schemas.GitLab, t =>
            t.HasCheckConstraint("ck_connections_status", CheckConstraints.In("status", Enum.GetNames<GitLabConnectionStatus>())));
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(GitLabConnection.NameMaxLength);
        builder.Property(c => c.BaseUrl).HasMaxLength(GitLabConnection.BaseUrlMaxLength);
        builder.Property(c => c.TokenHint).HasMaxLength(10);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.LastError).HasMaxLength(500);
        builder.Property(c => c.Version).IsRowVersion();
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => c.Name).IsUnique();
    }
}

internal sealed class GitLabProjectLinkConfiguration : IEntityTypeConfiguration<GitLabProjectLink>
{
    public void Configure(EntityTypeBuilder<GitLabProjectLink> builder)
    {
        builder.ToTable("project_links", Schemas.GitLab);
        builder.HasKey(l => l.Id);
        builder.Property(l => l.GitLabProjectId).HasColumnName("gitlab_project_id");
        builder.Property(l => l.PathWithNamespace).HasMaxLength(300);
        builder.Property(l => l.WebUrl).HasMaxLength(500);
        builder.Property(l => l.WebhookSecretHash).HasMaxLength(32);
        builder.HasOne<Project>().WithMany().HasForeignKey(l => l.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GitLabConnection>().WithMany().HasForeignKey(l => l.ConnectionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(l => l.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(l => l.ProjectId).IsUnique();
        builder.HasIndex(l => new { l.ConnectionId, l.GitLabProjectId });
    }
}

internal sealed class GitLabIssueLinkConfiguration : IEntityTypeConfiguration<GitLabIssueLink>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<GitLabIssueLink> builder)
    {
        builder.ToTable("issue_links", Schemas.GitLab, t =>
            t.HasCheckConstraint("ck_issue_links_state", CheckConstraints.In("state", Enum.GetNames<GitLabIssueState>())));
        builder.HasKey(l => l.Id);
        builder.Property(l => l.WebUrl).HasMaxLength(500);
        builder.Property(l => l.GitLabProjectId).HasColumnName("gitlab_project_id");
        builder.Property(l => l.GitLabUpdatedAt).HasColumnName("gitlab_updated_at");
        builder.Property(l => l.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.PendingChanges)
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, Json),
                v => v == null ? null : JsonSerializer.Deserialize<GitLabPendingChanges>(v, Json),
                new ValueComparer<GitLabPendingChanges?>((a, b) => Equals(a, b), v => v == null ? 0 : v.GetHashCode(), v => v));
        builder.HasOne<TaskItem>().WithMany().HasForeignKey(l => l.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GitLabConnection>().WithMany().HasForeignKey(l => l.ConnectionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(l => l.TaskId).IsUnique();

        // FR-IMP-04: an issue is imported once per connection.
        builder.HasIndex(l => new { l.ConnectionId, l.GitLabProjectId, l.IssueIid }).IsUnique();
    }
}

internal sealed class ImportRunConfiguration : IEntityTypeConfiguration<ImportRun>
{
    public void Configure(EntityTypeBuilder<ImportRun> builder)
    {
        builder.ToTable("import_runs", Schemas.GitLab, t =>
            t.HasCheckConstraint("ck_import_runs_kind", CheckConstraints.In("kind", Enum.GetNames<ImportRunKind>())));
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Errors).HasColumnType("text[]");
        builder.HasOne<Project>().WithMany().HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.StartedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => new { r.ProjectId, r.StartedAt });
    }
}
