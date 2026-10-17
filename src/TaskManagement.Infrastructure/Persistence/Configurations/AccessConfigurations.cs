using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskManagement.Domain.Access;

namespace TaskManagement.Infrastructure.Persistence.Configurations;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles", Schemas.Access);
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).HasMaxLength(Role.NameMaxLength);
        builder.HasIndex(r => r.Name).IsUnique();
        builder.Property(r => r.Description).HasMaxLength(Role.DescriptionMaxLength);
        builder.Property(r => r.Version).IsRowVersion();
        builder.HasMany(r => r.Permissions).WithOne().HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Permissions).HasField("_permissions").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions", Schemas.Access);
        builder.HasKey(p => p.Code);
        builder.Property(p => p.Code).HasMaxLength(Permission.CodeMaxLength);
        builder.Property(p => p.Module).HasMaxLength(50);
        builder.Property(p => p.Description).HasMaxLength(300);
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions", Schemas.Access, t =>
            t.HasCheckConstraint("ck_role_permissions_scope", "scope IN ('Own', 'Project', 'Global')"));
        builder.HasKey(p => new { p.RoleId, p.PermissionCode });
        builder.Property(p => p.PermissionCode).HasMaxLength(Permission.CodeMaxLength);
        builder.Property(p => p.Scope).HasConversion<string>().HasMaxLength(10);
        builder.HasOne<Permission>().WithMany().HasForeignKey(p => p.PermissionCode).OnDelete(DeleteBehavior.Restrict);
    }
}
