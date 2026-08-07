using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class QualityTierListRankConfiguration : IEntityTypeConfiguration<QualityTierListRank>
{
    public void Configure(EntityTypeBuilder<QualityTierListRank> builder)
    {
        builder.ToTable("quality_tierListRank");
        builder.HasKey(e => new { e.QualityTierListId, e.QualityTierId });
        builder.HasOne<QualityTierList>()
            .WithMany(e => e.QualityTierRanks)
            .HasForeignKey(e => e.QualityTierListId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.QualityTier)
            .WithMany()
            .HasForeignKey(e => e.QualityTierId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.QualityTierListId, e.Ordinal });
    }
}
