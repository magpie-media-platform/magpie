using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class DownloadConfiguration : IEntityTypeConfiguration<Download>
{
    public void Configure(EntityTypeBuilder<Download> builder)
    {
        builder.ToTable("download_download");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Progress).HasPrecision(5, 2);
        builder.HasOne(e => e.Release)
            .WithMany(e => e.Downloads)
            .HasForeignKey(e => e.ReleaseId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.ReleaseSource)
            .WithMany(e => e.Downloads)
            .HasForeignKey(e => e.ReleaseSourceId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<DownloadClient>()
            .WithMany()
            .HasForeignKey(e => e.DownloadClientId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.ReleaseId);
        builder.HasIndex(e => e.Status);
    }
}
