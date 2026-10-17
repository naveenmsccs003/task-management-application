using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskManagement.Domain.Audit;
using TaskManagement.Domain.Notifications;

namespace TaskManagement.Infrastructure.Persistence.Configurations;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_logs", Schemas.Audit);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).UseIdentityAlwaysColumn();
        builder.Property(e => e.ActorIp).HasMaxLength(45);
        builder.Property(e => e.Action).HasMaxLength(100);
        builder.Property(e => e.EntityType).HasMaxLength(100);
        builder.Property(e => e.EntityId).HasMaxLength(100);
        builder.Property(e => e.ChangesJson).HasColumnName("changes").HasColumnType("jsonb");
        builder.Property(e => e.CorrelationId).HasMaxLength(64);
        builder.HasIndex(e => e.OccurredAt);
        builder.HasIndex(e => new { e.EntityType, e.EntityId });
        builder.HasIndex(e => new { e.ActorId, e.OccurredAt });
        builder.HasIndex(e => new { e.ProjectId, e.OccurredAt });
    }
}

internal sealed class EmailDeliveryConfiguration : IEntityTypeConfiguration<EmailDelivery>
{
    public void Configure(EntityTypeBuilder<EmailDelivery> builder)
    {
        builder.ToTable("email_deliveries", Schemas.Notifications, t =>
            t.HasCheckConstraint("ck_email_deliveries_status", "status IN ('Pending', 'Sent', 'Failed')"));
        builder.HasKey(e => e.Id);
        builder.Property(e => e.RecipientEmail).HasMaxLength(256);
        builder.Property(e => e.Subject).HasMaxLength(300);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(e => e.LastError).HasMaxLength(1000);
        builder.HasIndex(e => e.NextAttemptAt).HasFilter("status = 'Pending'");
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications", Schemas.Notifications, t =>
            t.HasCheckConstraint("ck_notifications_type", CheckConstraints.In("type", Enum.GetNames<NotificationType>())));
        builder.HasKey(n => n.Id);
        builder.Ignore(n => n.IsRead);
        builder.Property(n => n.Type).HasConversion<string>().HasMaxLength(40);
        builder.Property(n => n.Title).HasMaxLength(Notification.TitleMaxLength);
        builder.Property(n => n.Body).HasMaxLength(Notification.BodyMaxLength);
        builder.Property(n => n.Link).HasMaxLength(Notification.LinkMaxLength);
        builder.HasOne<Domain.Identity.User>().WithMany().HasForeignKey(n => n.RecipientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(n => new { n.RecipientId, n.CreatedAt }).IsDescending(false, true);
        builder.HasIndex(n => n.RecipientId).HasFilter("read_at IS NULL").HasDatabaseName("ix_notifications_unread");
        builder.HasIndex(n => n.CreatedAt);
    }
}

internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("notification_preferences", Schemas.Notifications, t =>
            t.HasCheckConstraint("ck_notification_preferences_category", CheckConstraints.In("category", Enum.GetNames<NotificationCategory>())));
        builder.HasKey(p => new { p.UserId, p.Category });
        builder.Property(p => p.Category).HasConversion<string>().HasMaxLength(30);
        builder.HasOne<Domain.Identity.User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class JobRunConfiguration : IEntityTypeConfiguration<Domain.Operations.JobRun>
{
    public void Configure(EntityTypeBuilder<Domain.Operations.JobRun> builder)
    {
        builder.ToTable("job_runs", Schemas.Ops);
        builder.HasKey(j => j.JobName);
        builder.Property(j => j.JobName).HasMaxLength(Domain.Operations.JobRun.NameMaxLength);
        builder.Property(j => j.LastMessage).HasMaxLength(Domain.Operations.JobRun.MessageMaxLength);
    }
}
