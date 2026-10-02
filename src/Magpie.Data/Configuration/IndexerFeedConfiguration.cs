using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class IndexerFeedConfiguration : IEntityTypeConfiguration<IndexerFeed>
{
    public void Configure(EntityTypeBuilder<IndexerFeed> builder)
    {
        builder.ToTable("indexer_feed");
        builder.Property(e => e.Settings).UseStringDictionaryConversion();
    }
}
