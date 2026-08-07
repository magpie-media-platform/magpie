using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class MediaServerConfiguration : IEntityTypeConfiguration<MediaServer>
{
    public void Configure(EntityTypeBuilder<MediaServer> builder)
    {
        builder.ToTable("media_server");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Settings).UseStringDictionaryConversion();
        builder.Property(e => e.LastError).HasMaxLength(1024);
        builder.Property(e => e.ConsecutiveFailures).HasDefaultValue(0);
        builder.HasMany<Tag>()
            .WithMany(e => e.MediaServers)
            .UsingEntity(j => j.ToTable("media_serverTag"));
    }
}
