using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class ExtensionPackageSourceConfiguration : IEntityTypeConfiguration<ExtensionPackageSource>
{
    public void Configure(EntityTypeBuilder<ExtensionPackageSource> builder)
    {
        builder.ToTable("extension_packageSource");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Name).IsRequired();
        builder.Property(e => e.SourceUrl).IsRequired();
        builder.Property(e => e.Enabled).IsRequired();
        builder.Property(e => e.SearchFilterKind).IsRequired();
        builder.Property(e => e.CredentialKind).IsRequired();
        builder.HasIndex(e => e.Name).IsUnique();
        builder.HasIndex(e => e.Enabled);
    }
}
