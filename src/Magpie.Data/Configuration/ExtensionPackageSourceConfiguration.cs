using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class ExtensionPackageSourceConfiguration : IEntityTypeConfiguration<ExtensionPackageSource>
{
    public void Configure(EntityTypeBuilder<ExtensionPackageSource> builder)
    {
        builder.ToTable("extension_packageSource");
        builder.HasIndex(e => e.Name).IsUnique();
        builder.HasIndex(e => e.Enabled);
    }
}
