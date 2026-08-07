using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class ExternalIdConfiguration : IEntityTypeConfiguration<ExternalId>
{
    public void Configure(EntityTypeBuilder<ExternalId> builder)
    {
        builder.ToTable("metadata_externalId");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasOne<MetadataContribution>()
            .WithMany(e => e.ExternalIds)
            .HasForeignKey(e => e.MetadataContributionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.MetadataContributionId);
        builder.HasIndex(e => e.ReleaseId);
    }
}
