using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class ReleaseConfiguration : IEntityTypeConfiguration<Release>
{
    public void Configure(EntityTypeBuilder<Release> builder)
    {
        builder.ToTable("indexer_release");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasOne<Indexer>()
            .WithMany()
            .HasForeignKey(e => e.IndexerId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasMany<Tag>()
            .WithMany(e => e.Releases)
            .UsingEntity(j => j.ToTable("indexer_releaseTag"));
        builder.HasMany(e => e.ExternalIds)
            .WithOne()
            .HasForeignKey(e => e.ReleaseId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.IndexerId);
        builder.HasIndex(e => e.ObservedAt);
        builder.HasIndex(e => e.PublishDate);
    }
}
