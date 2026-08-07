using System;

using Magpie.Models.Data;
using Magpie.Models.Enums;

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

        builder.HasData(new ExtensionPackageSource
        {
            Id = new Guid("5d9a2b5f-5c59-4a1f-8c70-8011a8b4e0b1"),
            Name = "nuget.org",
            SourceUrl = "https://api.nuget.org/v3/index.json",
            Enabled = true,
            SearchFilterKind = ExtensionPackageSourceSearchFilterKind.Tag,
            SearchFilterValue = "magpie",
            CredentialKind = ExtensionPackageSourceCredentialKind.None,
            CreatedAt = DateTime.UnixEpoch
        });
    }
}
