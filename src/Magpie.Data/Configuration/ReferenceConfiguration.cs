using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class ReferenceConfiguration : IEntityTypeConfiguration<Reference>
{
    public void Configure(EntityTypeBuilder<Reference> builder)
    {
        builder.ToTable("metadata_reference");
        builder.HasOne<MetadataContribution>()
            .WithMany(e => e.References)
            .HasForeignKey(e => e.MetadataContributionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
