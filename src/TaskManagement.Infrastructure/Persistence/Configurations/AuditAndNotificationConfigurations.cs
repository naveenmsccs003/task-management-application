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
