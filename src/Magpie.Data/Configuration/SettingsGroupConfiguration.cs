using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class SettingsGroupConfiguration : IEntityTypeConfiguration<SettingsGroup>
{
    public void Configure(EntityTypeBuilder<SettingsGroup> builder)
    {
        builder.ToTable("configuration_settingsGroup");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasIndex(e => new { e.ExtensionId, e.Name }).IsUnique();
        builder.HasOne<Extension>()
            .WithMany()
            .HasForeignKey(e => e.ExtensionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
