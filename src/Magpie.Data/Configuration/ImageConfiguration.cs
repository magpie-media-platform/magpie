using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class ImageConfiguration : IEntityTypeConfiguration<Image>
{
    public void Configure(EntityTypeBuilder<Image> builder)
    {
        builder.ToTable("metadata_image");
        builder.Property(e => e.IsPrimary).HasDefaultValue(false);
        builder.HasOne<MetadataContribution>()
            .WithMany(e => e.Images)
            .HasForeignKey(e => e.MetadataContributionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
