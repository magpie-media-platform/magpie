using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class RelationshipConfiguration : IEntityTypeConfiguration<Relationship>
{
    public void Configure(EntityTypeBuilder<Relationship> builder)
    {
        builder.ToTable("metadata_relationship");
        builder.HasOne<MetadataContribution>()
            .WithMany(e => e.Relationships)
            .HasForeignKey(e => e.MetadataContributionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.RelatedMetadataContainer)
            .WithMany()
            .HasForeignKey(e => e.RelatedMetadataContainerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.Kind);
    }
}
