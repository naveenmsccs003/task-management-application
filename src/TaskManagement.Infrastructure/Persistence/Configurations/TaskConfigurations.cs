using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskManagement.Domain.Checklists;
using TaskManagement.Domain.Identity;
using TaskManagement.Domain.Projects;
using TaskManagement.Domain.Tasks;

namespace TaskManagement.Infrastructure.Persistence.Configurations;

internal sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("tasks", Schemas.Tasks, t =>
        {
            t.HasCheckConstraint("ck_tasks_status", CheckConstraints.In("status", Enum.GetNames<TaskItemStatus>()));
            t.HasCheckConstraint("ck_tasks_type", CheckConstraints.In("type", Enum.GetNames<TaskType>()));
            t.HasCheckConstraint("ck_tasks_priority", CheckConstraints.In("priority", Enum.GetNames<TaskPriority>()));
            t.HasCheckConstraint("ck_tasks_allocated_minutes", "allocated_minutes IS NULL OR allocated_minutes > 0");
            t.HasCheckConstraint("ck_tasks_due_after_start", "due_date IS NULL OR start_date IS NULL OR due_date >= start_date");
        });
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Key).HasMaxLength(Project.KeyMaxLength + 11);
        builder.HasIndex(t => t.Key).IsUnique();
        builder.HasIndex(t => new { t.ProjectId, t.Number }).IsUnique();
        builder.Property(t => t.Title).HasMaxLength(TaskItem.TitleMaxLength);
        builder.Property(t => t.Description).HasColumnType("text");
        builder.Property(t => t.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Priority).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(t => t.Labels).HasColumnType("text[]");
        builder.Property(t => t.CancelReason).HasMaxLength(TaskItem.ReasonMaxLength);
        builder.Property(t => t.Version).IsRowVersion();

        builder.HasOne<Project>().WithMany().HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.AssigneeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UpdatedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.CancelledBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ChecklistTemplate>().WithMany().HasForeignKey(t => t.ChecklistTemplateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AlgorithmSubmission>().WithMany().HasForeignKey(t => t.AlgorithmApprovedSubmissionId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.StatusHistory).WithOne().HasForeignKey(h => h.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(t => t.StatusHistory).HasField("_statusHistory").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(t => t.Assignments).WithOne().HasForeignKey(a => a.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(t => t.Assignments).HasField("_assignments").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(t => new { t.ProjectId, t.Status });
        builder.HasIndex(t => new { t.AssigneeId, t.Status });
        builder.HasIndex(t => t.DueDate).HasFilter("status NOT IN ('Approved', 'Closed', 'Cancelled')");
        // A trigram index on lower(title) for title search is created in the migration (EF cannot express it).
    }
}

internal sealed class TaskStatusChangeConfiguration : IEntityTypeConfiguration<TaskStatusChange>
{
    public void Configure(EntityTypeBuilder<TaskStatusChange> builder)
    {
        builder.ToTable("task_status_history", Schemas.Tasks, t =>
        {
            t.HasCheckConstraint("ck_task_status_history_to_status", CheckConstraints.In("to_status", Enum.GetNames<TaskItemStatus>()));
            t.HasCheckConstraint("ck_task_status_history_action", CheckConstraints.In("action", Enum.GetNames<TaskAction>()));
        });
        builder.HasKey(h => h.Id);
        // Ids are created in the domain; ValueGeneratedNever makes EF insert new rows found through the parent's collection.
        builder.Property(h => h.Id).ValueGeneratedNever();
        builder.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(30);
        builder.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(30);
        builder.Property(h => h.Action).HasConversion<string>().HasMaxLength(30);
        builder.Property(h => h.Comment).HasMaxLength(TaskItem.ReasonMaxLength);
        builder.HasOne<User>().WithMany().HasForeignKey(h => h.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(h => new { h.TaskId, h.OccurredAt });
    }
}

internal sealed class TaskAssignmentConfiguration : IEntityTypeConfiguration<TaskAssignment>
{
    public void Configure(EntityTypeBuilder<TaskAssignment> builder)
    {
        builder.ToTable("task_assignments", Schemas.Tasks, t =>
            t.HasCheckConstraint("ck_task_assignments_kind", CheckConstraints.In("kind", Enum.GetNames<AssignmentKind>())));
        builder.HasKey(a => a.Id);
        // Ids are created in the domain; ValueGeneratedNever makes EF insert new rows found through the parent's collection.
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Reason).HasMaxLength(TaskItem.ReasonMaxLength);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.FromUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.ToUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.AssignedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.TaskId, a.OccurredAt });

        // "Tasks I am or was assigned to" (FR-TASK-06).
        builder.HasIndex(a => new { a.ToUserId, a.TaskId });
    }
}

internal sealed class TaskCommentConfiguration : IEntityTypeConfiguration<TaskComment>
{
    public void Configure(EntityTypeBuilder<TaskComment> builder)
    {
        builder.ToTable("task_comments", Schemas.Tasks);
        builder.HasKey(c => c.Id);
        builder.Ignore(c => c.IsDeleted);
        builder.Property(c => c.Body).HasColumnType("text");
        builder.HasOne<TaskItem>().WithMany().HasForeignKey(c => c.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.AuthorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.DeletedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(c => c.Edits).WithOne().HasForeignKey(e => e.CommentId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(c => c.Edits).HasField("_edits").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(c => new { c.TaskId, c.CreatedAt });
    }
}

internal sealed class TaskCommentEditConfiguration : IEntityTypeConfiguration<TaskCommentEdit>
{
    public void Configure(EntityTypeBuilder<TaskCommentEdit> builder)
    {
        builder.ToTable("task_comment_edits", Schemas.Tasks);
        builder.HasKey(e => e.Id);
        // Ids are created in the domain; ValueGeneratedNever makes EF insert new rows found through the parent's collection.
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.PreviousBody).HasColumnType("text");
    }
}
