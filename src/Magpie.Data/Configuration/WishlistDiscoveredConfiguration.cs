using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class WishlistDiscoveredConfiguration : IEntityTypeConfiguration<WishlistDiscovered>
{
    public void Configure(EntityTypeBuilder<WishlistDiscovered> builder)
    {
        builder.ToTable("wishlist_discovered");
        builder.Property(e => e.ExternalIdSource).IsRequired().HasMaxLength(255);
        builder.Property(e => e.ExternalId).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Title).HasMaxLength(1024);
        builder.HasOne<Wishlist>()
            .WithMany()
            .HasForeignKey(e => e.WishlistId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.WishlistId, e.ExternalIdSource, e.ExternalId }).IsUnique();
        builder.HasIndex(e => e.WishlistId);
    }
}
