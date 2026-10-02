using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class AlternateTextConfiguration : IEntityTypeConfiguration<AlternateText>
{
    public void Configure(EntityTypeBuilder<AlternateText> builder)
    {
        builder.ToTable("metadata_alternateText");
        builder.HasOne<MetadataContribution>()
            .WithMany(e => e.AlternateTexts)
            .HasForeignKey(e => e.MetadataContributionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
