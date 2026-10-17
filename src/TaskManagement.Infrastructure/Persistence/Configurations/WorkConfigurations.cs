using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskManagement.Domain.Checklists;
using TaskManagement.Domain.Identity;
using TaskManagement.Domain.Operations;
using TaskManagement.Domain.Projects;
using TaskManagement.Domain.Tasks;
using TaskManagement.Domain.Time;

namespace TaskManagement.Infrastructure.Persistence.Configurations;

internal sealed class AlgorithmSubmissionConfiguration : IEntityTypeConfiguration<AlgorithmSubmission>
{
    public void Configure(EntityTypeBuilder<AlgorithmSubmission> builder)
    {
        builder.ToTable("algorithm_submissions", Schemas.Tasks, t =>
            t.HasCheckConstraint("ck_algorithm_submissions_decision", CheckConstraints.In("decision", Enum.GetNames<AlgorithmDecision>())));
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Content).HasColumnType("text");
        builder.Property(a => a.Decision).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.DecisionComment).HasMaxLength(AlgorithmSubmission.CommentMaxLength);
        builder.HasOne<TaskItem>().WithMany().HasForeignKey(a => a.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.SubmittedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.DecidedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.TaskId, a.Version }).IsUnique();

        // At most one version waits for a decision at a time.
        builder.HasIndex(a => a.TaskId).IsUnique().HasFilter("decision = 'Pending'").HasDatabaseName("ux_algorithm_submissions_pending");
    }
}

internal sealed class ChecklistTemplateConfiguration : IEntityTypeConfiguration<ChecklistTemplate>
{
    public void Configure(EntityTypeBuilder<ChecklistTemplate> builder)
    {
        builder.ToTable("checklist_templates", Schemas.Tasks);
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).HasMaxLength(ChecklistTemplate.NameMaxLength);
        builder.Property(t => t.Version).IsRowVersion();
        builder.HasOne<Project>().WithMany().HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UpdatedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(t => t.Items).WithOne().HasForeignKey(i => i.TemplateId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(t => t.ProjectId);
    }
}

internal sealed class ChecklistTemplateItemConfiguration : IEntityTypeConfiguration<ChecklistTemplateItem>
{
    public void Configure(EntityTypeBuilder<ChecklistTemplateItem> builder)
    {
        builder.ToTable("checklist_template_items", Schemas.Tasks);
        builder.HasKey(i => i.Id);

        // Ids are created in the domain; ValueGeneratedNever makes EF insert new rows found through the template's collection.
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Text).HasMaxLength(ChecklistTemplate.ItemTextMaxLength);
    }
}

internal sealed class TaskChecklistItemConfiguration : IEntityTypeConfiguration<TaskChecklistItem>
{
    public void Configure(EntityTypeBuilder<TaskChecklistItem> builder)
    {
        builder.ToTable("task_checklist_items", Schemas.Tasks);
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Text).HasMaxLength(ChecklistTemplate.ItemTextMaxLength);
        builder.Property(i => i.Note).HasMaxLength(TaskChecklistItem.NoteMaxLength);
        builder.Property(i => i.Version).IsRowVersion();
        builder.HasOne<TaskItem>().WithMany().HasForeignKey(i => i.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(i => i.CompletedBy).OnDelete(DeleteBehavior.Restrict);

        // No FK to template items: templates can change freely after their items were copied (FR-CHK-02).
        builder.HasIndex(i => new { i.TaskId, i.SortOrder });
    }
}

internal sealed class WorkSessionConfiguration : IEntityTypeConfiguration<WorkSession>
{
    public void Configure(EntityTypeBuilder<WorkSession> builder)
    {
        builder.ToTable("work_sessions", Schemas.Time, t =>
        {
            t.HasCheckConstraint("ck_work_sessions_end_after_start", "ended_at IS NULL OR ended_at >= started_at");
            t.HasCheckConstraint("ck_work_sessions_end_reason", CheckConstraints.In("end_reason", Enum.GetNames<SessionEndReason>()));
        });
        builder.HasKey(s => s.Id);
        builder.Ignore(s => s.IsActive);
        builder.Property(s => s.EndReason).HasConversion<string>().HasMaxLength(30);
        builder.HasOne<TaskItem>().WithMany().HasForeignKey(s => s.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Restrict);

        // FR-TIME-03: one active session per member and per task, enforced by the database.
        builder.HasIndex(s => s.UserId).IsUnique().HasFilter("ended_at IS NULL").HasDatabaseName("ux_work_sessions_active_user");
        builder.HasIndex(s => s.TaskId).IsUnique().HasFilter("ended_at IS NULL").HasDatabaseName("ux_work_sessions_active_task");
        builder.HasIndex(s => new { s.TaskId, s.Revision });
        builder.HasIndex(s => new { s.UserId, s.StartedAt });
    }
}

internal sealed class TimeAdjustmentConfiguration : IEntityTypeConfiguration<TimeAdjustment>
{
    public void Configure(EntityTypeBuilder<TimeAdjustment> builder)
    {
        builder.ToTable("time_adjustments", Schemas.Time, t =>
            t.HasCheckConstraint("ck_time_adjustments_minutes", "minutes <> 0"));
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Reason).HasMaxLength(TimeAdjustment.ReasonMaxLength);
        builder.HasOne<TaskItem>().WithMany().HasForeignKey(a => a.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.TaskId, a.Revision });
    }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_keys", Schemas.Ops);
        builder.HasKey(r => new { r.UserId, r.Key });
        builder.Property(r => r.Endpoint).HasMaxLength(IdempotencyRecord.EndpointMaxLength);
        builder.Property(r => r.ResponseBody).HasColumnType("jsonb");
        builder.HasIndex(r => r.CreatedAt);
    }
}

internal sealed class ReviewSubmissionConfiguration : IEntityTypeConfiguration<ReviewSubmission>
{
    public void Configure(EntityTypeBuilder<ReviewSubmission> builder)
    {
        builder.ToTable("review_submissions", Schemas.Tasks, t =>
            t.HasCheckConstraint("ck_review_submissions_decision", CheckConstraints.In("decision", Enum.GetNames<ReviewDecision>())));
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Summary).HasColumnType("text");
        builder.Property(r => r.Decision).HasConversion<string>().HasMaxLength(30);
        builder.Property(r => r.Comment).HasMaxLength(ReviewSubmission.CommentMaxLength);
        builder.HasOne<TaskItem>().WithMany().HasForeignKey(r => r.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.SubmittedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.ReviewedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => new { r.TaskId, r.Revision });

        // One review waits for a decision at a time.
        builder.HasIndex(r => r.TaskId).IsUnique().HasFilter("decision = 'Pending'").HasDatabaseName("ux_review_submissions_pending");
    }
}

internal sealed class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> builder)
    {
        builder.ToTable("approval_requests", Schemas.Tasks, t =>
        {
            t.HasCheckConstraint("ck_approval_requests_type", CheckConstraints.In("type", Enum.GetNames<ApprovalRequestType>()));
            t.HasCheckConstraint("ck_approval_requests_status", CheckConstraints.In("status", Enum.GetNames<ApprovalRequestStatus>()));
            t.HasCheckConstraint("ck_approval_requests_reason", $"char_length(reason) >= {ApprovalRequest.ReasonMinLength}");
        });
        builder.HasKey(r => r.Id);
        builder.Ignore(r => r.IsPending);
        builder.Property(r => r.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Reason).HasMaxLength(ApprovalRequest.ReasonMaxLength);
        builder.Property(r => r.DecisionComment).HasMaxLength(ApprovalRequest.CommentMaxLength);
        builder.Property(r => r.Version).IsRowVersion();
        builder.HasOne<TaskItem>().WithMany().HasForeignKey(r => r.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.RequestedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.ProposedAssigneeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.DecidedBy).OnDelete(DeleteBehavior.Restrict);

        // FR-REV-06, FR-DL-04: one pending request of each type per task.
        builder.HasIndex(r => new { r.TaskId, r.Type }).IsUnique().HasFilter("status = 'Pending'").HasDatabaseName("ux_approval_requests_pending");
        builder.HasIndex(r => new { r.Status, r.RequestedAt });
    }
}

internal sealed class DelayReportConfiguration : IEntityTypeConfiguration<DelayReport>
{
    public void Configure(EntityTypeBuilder<DelayReport> builder)
    {
        builder.ToTable("delay_reports", Schemas.Tasks, t =>
            t.HasCheckConstraint("ck_delay_reports_category", CheckConstraints.In("category", Enum.GetNames<DelayCategory>())));
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Category).HasConversion<string>().HasMaxLength(30);
        builder.Property(d => d.Description).HasMaxLength(DelayReport.DescriptionMaxLength);
        builder.HasOne<TaskItem>().WithMany().HasForeignKey(d => d.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(d => d.ReportedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => new { d.TaskId, d.ReportedAt });
    }
}
