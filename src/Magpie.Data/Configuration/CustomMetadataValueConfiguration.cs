using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class CustomMetadataValueConfiguration : IEntityTypeConfiguration<CustomMetadataValue>
{
    public void Configure(EntityTypeBuilder<CustomMetadataValue> builder)
    {
        builder.ToTable("metadata_customValue");
        builder.HasOne<MetadataContribution>()
            .WithMany(e => e.CustomMetadata)
            .HasForeignKey(e => e.MetadataContributionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.MetadataContributionId, e.Key }).IsUnique();
    }
}
