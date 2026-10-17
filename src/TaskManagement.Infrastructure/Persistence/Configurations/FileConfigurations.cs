using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskManagement.Domain.Files;
using TaskManagement.Domain.Identity;
using TaskManagement.Domain.Projects;
using TaskManagement.Domain.Tasks;

namespace TaskManagement.Infrastructure.Persistence.Configurations;

internal sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("attachments", Schemas.Files, t =>
        {
            t.HasCheckConstraint("ck_attachments_owner_type", CheckConstraints.In("owner_type", Enum.GetNames<AttachmentOwnerType>()));
            t.HasCheckConstraint("ck_attachments_scan_status", CheckConstraints.In("scan_status", Enum.GetNames<AttachmentScanStatus>()));
            t.HasCheckConstraint("ck_attachments_size", "size_bytes > 0");
        });
        builder.HasKey(a => a.Id);
        builder.Property(a => a.OwnerType).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.ScanStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.OriginalFileName).HasMaxLength(Attachment.FileNameMaxLength);
        builder.Property(a => a.ContentType).HasMaxLength(100);
        builder.Property(a => a.FileType).HasMaxLength(10);
        builder.Property(a => a.Sha256).HasMaxLength(32);
        builder.Property(a => a.StorageKey).HasMaxLength(200);
        builder.HasOne<Project>().WithMany().HasForeignKey(a => a.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TaskItem>().WithMany().HasForeignKey(a => a.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.UploadedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.DeletedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.StorageKey).IsUnique();
        builder.HasIndex(a => new { a.TaskId, a.UploadedAt });
        builder.HasIndex(a => new { a.OwnerType, a.OwnerId });

        // The scan job and the draft clean-up look only at these few rows.
        builder.HasIndex(a => a.UploadedAt).HasFilter("scan_status = 'Pending'").HasDatabaseName("ix_attachments_pending_scan");
        builder.HasIndex(a => a.UploadedAt).HasFilter("owner_id IS NULL").HasDatabaseName("ix_attachments_drafts");
    }
}
