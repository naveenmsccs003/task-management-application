using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Identity;

namespace TaskManagement.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", Schemas.Identity);
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Email).HasMaxLength(User.EmailMaxLength);
        builder.Property(u => u.NormalizedEmail).HasMaxLength(User.EmailMaxLength);
        builder.HasIndex(u => u.NormalizedEmail).IsUnique();
        builder.Property(u => u.FullName).HasMaxLength(User.FullNameMaxLength);
        builder.Property(u => u.TimeZone).HasMaxLength(User.TimeZoneMaxLength);
        builder.Property(u => u.EmployeeCode).HasMaxLength(User.EmployeeCodeMaxLength);
        builder.HasIndex(u => u.EmployeeCode).IsUnique().HasFilter("employee_code IS NOT NULL");
        builder.Property(u => u.PasswordHash).HasMaxLength(500);
        builder.Property(u => u.SecurityStamp).HasMaxLength(64);
        builder.Property(u => u.Version).IsRowVersion();
        builder.HasOne<Role>().WithMany().HasForeignKey(u => u.GlobalRoleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(u => u.GlobalRoleId);
    }
}

internal sealed class PasswordTokenConfiguration : IEntityTypeConfiguration<PasswordToken>
{
    public void Configure(EntityTypeBuilder<PasswordToken> builder)
    {
        builder.ToTable("password_tokens", Schemas.Identity, t =>
            t.HasCheckConstraint("ck_password_tokens_purpose", "purpose IN ('Reset', 'Invite')"));
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Purpose).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.TokenHash).HasMaxLength(32);
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.UserId).HasFilter("used_at IS NULL AND revoked_at IS NULL");
        builder.Property(t => t.RequestedIp).HasMaxLength(45);
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
