using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class AwardConfiguration : IEntityTypeConfiguration<Award>
{
    public void Configure(EntityTypeBuilder<Award> builder)
    {
        builder.ToTable("metadata_award");
        builder.HasOne<MetadataContribution>()
            .WithMany(e => e.Awards)
            .HasForeignKey(e => e.MetadataContributionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
