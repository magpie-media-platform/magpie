using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class ReleaseIdentityKeyConfiguration : IEntityTypeConfiguration<ReleaseIdentityKey>
{
    public void Configure(EntityTypeBuilder<ReleaseIdentityKey> builder)
    {
        builder.ToTable("indexer_releaseIdentityKey");
        builder.Property(e => e.Authority).HasMaxLength(255).IsRequired();
        builder.Property(e => e.Value).HasMaxLength(1024).IsRequired();
        builder.HasOne(e => e.Release)
            .WithMany(e => e.IdentityKeys)
            .HasForeignKey(e => e.ReleaseId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Download)
            .WithMany(e => e.ReleaseIdentityKeys)
            .HasForeignKey(e => e.DownloadId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.ReleaseId);
        builder.HasIndex(e => e.DownloadId);
        builder.HasIndex(e => new { e.ReleaseId, e.Authority, e.Value });
        builder.HasIndex(e => new { e.DownloadId, e.Authority, e.Value });
    }
}
