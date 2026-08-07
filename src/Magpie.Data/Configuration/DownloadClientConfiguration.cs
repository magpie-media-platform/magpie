using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class DownloadClientConfiguration : IEntityTypeConfiguration<DownloadClient>
{
    public void Configure(EntityTypeBuilder<DownloadClient> builder)
    {
        builder.ToTable("download_client");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Settings).UseStringDictionaryConversion();
        builder.Property(e => e.Priority).HasDefaultValue(100);
        builder.Property(e => e.LastError).HasMaxLength(1024);
        builder.Property(e => e.ConsecutiveFailures).HasDefaultValue(0);
        builder.Property(e => e.CleanupDeleteFiles).HasDefaultValue(true);
        builder.Property(e => e.KeepFailedOnClient).HasDefaultValue(false);
        builder.HasMany<Tag>()
            .WithMany(e => e.DownloadClients)
            .UsingEntity(j => j.ToTable("download_clientTag"));
    }
}
