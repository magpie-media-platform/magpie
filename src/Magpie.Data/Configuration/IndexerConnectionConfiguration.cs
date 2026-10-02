using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class IndexerConnectionConfiguration : IEntityTypeConfiguration<IndexerConnection>
{
    public void Configure(EntityTypeBuilder<IndexerConnection> builder)
    {
        builder.ToTable("indexer_connection");
        builder.Property(e => e.Settings).UseStringDictionaryConversion();
        builder.Property(e => e.LastError).HasMaxLength(1024);
        builder.Property(e => e.ConsecutiveFailures).HasDefaultValue(0);
        builder.HasOne<Indexer>()
            .WithMany(e => e.Connections)
            .HasForeignKey(e => e.IndexerId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany<Tag>()
            .WithMany(e => e.IndexerConnections)
            .UsingEntity(j => j.ToTable("indexer_connectionTag"));
    }
}
