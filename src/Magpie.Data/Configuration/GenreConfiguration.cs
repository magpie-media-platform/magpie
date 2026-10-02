using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class GenreConfiguration : IEntityTypeConfiguration<Genre>
{
    public void Configure(EntityTypeBuilder<Genre> builder)
    {
        builder.ToTable("metadata_genre");
        builder.HasOne<MetadataContribution>()
            .WithMany(e => e.Genres)
            .HasForeignKey(e => e.MetadataContributionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
