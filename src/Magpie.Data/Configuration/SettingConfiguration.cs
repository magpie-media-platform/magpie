using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class SettingConfiguration : IEntityTypeConfiguration<Setting>
{
    public void Configure(EntityTypeBuilder<Setting> builder)
    {
        builder.ToTable("configuration_setting");
        builder.HasOne<SettingsGroup>()
            .WithMany(e => e.Settings)
            .HasForeignKey(e => e.SettingsGroupId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.SettingsGroupId, e.Key }).IsUnique();
    }
}
