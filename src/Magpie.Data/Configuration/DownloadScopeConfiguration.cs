using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class DownloadScopeConfiguration : IEntityTypeConfiguration<DownloadScope>
{
    public void Configure(EntityTypeBuilder<DownloadScope> builder)
    {
        builder.ToTable("download_downloadScope");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasOne(e => e.Download)
            .WithMany(e => e.Scope)
            .HasForeignKey(e => e.DownloadId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.ContentHierarchyItem)
            .WithMany()
            .HasForeignKey(e => e.ContentHierarchyItemId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.ContentHierarchyItemId);
        builder.HasIndex(e => new { e.DownloadId, e.ContentHierarchyItemId }).IsUnique();
    }
}
