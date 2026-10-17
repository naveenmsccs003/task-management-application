using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Checklists;
using TaskManagement.Domain.Identity;
using TaskManagement.Domain.Projects;

namespace TaskManagement.Infrastructure.Persistence.Configurations;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects", Schemas.Projects, t =>
        {
            t.HasCheckConstraint("ck_projects_status", CheckConstraints.In("status", Enum.GetNames<ProjectStatus>()));
            t.HasCheckConstraint("ck_projects_key", "key ~ '^[A-Z][A-Z0-9]{1,9}$'");
        });
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Key).HasMaxLength(Project.KeyMaxLength);
        builder.HasIndex(p => p.Key).IsUnique();
        builder.Property(p => p.Name).HasMaxLength(Project.NameMaxLength);
        builder.Property(p => p.Description).HasColumnType("text");
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Version).IsRowVersion();
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.UpdatedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ChecklistTemplate>().WithMany().HasForeignKey(p => p.DefaultChecklistTemplateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => p.Status);
    }
}

internal sealed class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> builder)
    {
        builder.ToTable("project_members", Schemas.Projects);
        builder.HasKey(m => m.Id);
        builder.Ignore(m => m.IsCurrent);
        builder.HasOne<Project>().WithMany().HasForeignKey(m => m.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Role>().WithMany().HasForeignKey(m => m.ProjectRoleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.AddedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.RemovedBy).OnDelete(DeleteBehavior.Restrict);

        // One current membership per member and project; removed rows stay as history.
        builder.HasIndex(m => new { m.ProjectId, m.UserId }).IsUnique().HasFilter("removed_at IS NULL");
        builder.HasIndex(m => m.UserId).HasFilter("removed_at IS NULL");
    }
}

/// <summary>
/// Per-project task number counter (one row per project). Kept out of <c>projects</c> so creating a task does not
/// change the project row's version and cause false "changed by someone else" conflicts on project edits.
/// </summary>
internal sealed class TaskCounter
{
    public Guid ProjectId { get; set; }

    public int LastNumber { get; set; }
}

internal sealed class TaskCounterConfiguration : IEntityTypeConfiguration<TaskCounter>
{
    public void Configure(EntityTypeBuilder<TaskCounter> builder)
    {
        builder.ToTable("task_counters", Schemas.Projects);
        builder.HasKey(c => c.ProjectId);
        builder.HasOne<Project>().WithMany().HasForeignKey(c => c.ProjectId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal static class CheckConstraints
{
    /// <summary><c>column IN ('A', 'B')</c> for enums stored as text (docs/database-design.md §1).</summary>
    public static string In(string column, IEnumerable<string> values) =>
        $"{column} IN ({string.Join(", ", values.Select(v => $"'{v}'"))})";
}
