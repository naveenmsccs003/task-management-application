using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskManagement.Domain.Settings;

namespace TaskManagement.Infrastructure.Persistence.Configurations;

internal sealed class GlobalSettingConfiguration : IEntityTypeConfiguration<GlobalSetting>
{
    public void Configure(EntityTypeBuilder<GlobalSetting> builder)
    {
        builder.ToTable("global_settings", Schemas.Settings);
        builder.HasKey(s => s.Key);
        builder.Property(s => s.Key).HasMaxLength(GlobalSetting.KeyMaxLength);
        builder.Property(s => s.ValueJson).HasColumnName("value").HasColumnType("jsonb");
        builder.Property(s => s.UpdatedAt);
        builder.Property(s => s.UpdatedBy);
    }
}
