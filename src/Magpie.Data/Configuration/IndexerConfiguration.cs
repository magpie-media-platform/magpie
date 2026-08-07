using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class IndexerConfiguration : IEntityTypeConfiguration<Indexer>
{
    public void Configure(EntityTypeBuilder<Indexer> builder)
    {
        builder.ToTable("indexer_indexer");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasIndex(e => e.ProviderType);
        builder.HasOne<IndexerFeed>()
            .WithMany()
            .HasForeignKey(e => e.FeedId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
