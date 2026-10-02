using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class ContentRatingConfiguration : IEntityTypeConfiguration<ContentRating>
{
    public void Configure(EntityTypeBuilder<ContentRating> builder)
    {
        builder.ToTable("metadata_contentRating");
        builder.HasOne<MetadataContribution>()
            .WithMany(e => e.ContentRatings)
            .HasForeignKey(e => e.MetadataContributionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
