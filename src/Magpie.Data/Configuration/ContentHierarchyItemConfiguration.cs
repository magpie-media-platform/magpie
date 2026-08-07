using System.Collections.Generic;
using System.Text.Json;

using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class ContentHierarchyItemConfiguration : IEntityTypeConfiguration<ContentHierarchyItem>
{
    public void Configure(EntityTypeBuilder<ContentHierarchyItem> builder)
    {
        builder.ToTable("content_hierarchyItem");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Settings).UseStringDictionaryConversion();
        builder.Property(e => e.LanguagePreferences).HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<List<string>>(v, JsonSerializerOptions.Default) ?? new())
            .UseListComparer();
        builder.Property(e => e.Ordinal);
        builder.Property(e => e.AlignmentKey);
        builder.Property(e => e.PendingMetadataRefresh).HasDefaultValue(false);
        builder.HasOne<ContentHierarchyItem>()
            .WithMany()
            .HasForeignKey(e => e.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.RootFolder)
            .WithMany()
            .HasForeignKey(e => e.RootFolderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.QualityTierList)
            .WithMany()
            .HasForeignKey(e => e.QualityTierListId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.MetadataContainer)
            .WithOne()
            .HasForeignKey<ContentHierarchyItem>(e => e.MetadataContainerId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.ParentId);
        builder.HasIndex(e => e.MetadataContainerId).IsUnique();
        builder.HasIndex(e => new { e.ContentType, e.Level });
        builder.HasIndex(e => new { e.ContentType, e.ParentId, e.Level, e.Slug }).IsUnique();
        builder.HasIndex(e => new { e.ContentType, e.Level, e.Slug })
            .IsUnique()
            .HasFilter("\"ParentId\" IS NULL");
        builder.HasIndex(e => e.EffectiveMonitoring);
        builder.HasIndex(e => e.EffectiveRootFolderId);
        builder.HasIndex(e => e.EffectiveQualityTierListId);
    }
}
