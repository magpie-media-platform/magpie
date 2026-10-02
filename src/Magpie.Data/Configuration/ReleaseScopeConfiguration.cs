using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class ReleaseScopeConfiguration : IEntityTypeConfiguration<ReleaseScope>
{
    public void Configure(EntityTypeBuilder<ReleaseScope> builder)
    {
        builder.ToTable("indexer_releaseScope");
        builder.HasOne(e => e.Release)
            .WithMany(e => e.Scope)
            .HasForeignKey(e => e.ReleaseId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.ContentHierarchyItem)
            .WithMany()
            .HasForeignKey(e => e.ContentHierarchyItemId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.ReleaseId, e.ContentHierarchyItemId }).IsUnique();
    }
}
