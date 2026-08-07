using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class ReleaseSourceConfiguration : IEntityTypeConfiguration<ReleaseSource>
{
    public void Configure(EntityTypeBuilder<ReleaseSource> builder)
    {
        builder.ToTable("indexer_releaseSource");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Key).HasMaxLength(64).IsRequired();
        builder.HasOne(e => e.Release)
            .WithMany(e => e.Sources)
            .HasForeignKey(e => e.ReleaseId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Indexer>()
            .WithMany()
            .HasForeignKey(e => e.IndexerId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(e => e.IndexerId);
        builder.HasIndex(e => e.ReleaseId);
        builder.HasIndex(e => new { e.ReleaseId, e.Key }).IsUnique();
    }
}
