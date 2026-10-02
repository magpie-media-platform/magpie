using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class MetadataContributionConfiguration : IEntityTypeConfiguration<MetadataContribution>
{
    public void Configure(EntityTypeBuilder<MetadataContribution> builder)
    {
        builder.ToTable("metadata_contribution");
        builder.HasOne(e => e.MetadataContainer)
            .WithMany(e => e.Contributions)
            .HasForeignKey(e => e.MetadataContainerId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.ProviderType);
    }
}
