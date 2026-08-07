using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class ContentItemConfiguration : IEntityTypeConfiguration<ContentItem>
{
    public void Configure(EntityTypeBuilder<ContentItem> builder)
    {
        builder.ToTable("content_item");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasOne<ContentHierarchyItem>()
            .WithOne(e => e.ContentItem)
            .HasForeignKey<ContentItem>(e => e.ContentHierarchyItemId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.ContentHierarchyItemId).IsUnique();
        builder.HasIndex(e => e.Fulfillment);
        builder.HasIndex(e => e.ActiveAcquisitionStatus);
        builder.HasIndex(e => e.QualityStatus);
    }
}
