using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class PreviewConfiguration : IEntityTypeConfiguration<Preview>
{
    public void Configure(EntityTypeBuilder<Preview> builder)
    {
        builder.ToTable("metadata_preview");
        builder.HasOne<MetadataContribution>()
            .WithMany(e => e.Previews)
            .HasForeignKey(e => e.MetadataContributionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
