using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class ExtensionConfiguration : IEntityTypeConfiguration<Extension>
{
    public void Configure(EntityTypeBuilder<Extension> builder)
    {
        builder.ToTable("extension_extension");
        builder.HasIndex(e => e.PackageId).IsUnique();
        builder.HasIndex(e => e.AssemblyName).IsUnique();
    }
}
