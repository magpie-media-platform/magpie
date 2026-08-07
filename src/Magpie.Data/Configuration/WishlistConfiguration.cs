using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class WishlistConfiguration : IEntityTypeConfiguration<Wishlist>
{
    public void Configure(EntityTypeBuilder<Wishlist> builder)
    {
        builder.ToTable("wishlist_wishlist");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasOne(e => e.ContentFilter)
            .WithMany()
            .HasForeignKey(e => e.ContentFilterId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.QualityTierList)
            .WithMany()
            .HasForeignKey(e => e.QualityTierListId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.RootFolder)
            .WithMany()
            .HasForeignKey(e => e.RootFolderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(e => e.Tags)
            .WithMany(e => e.Wishlists)
            .UsingEntity(
                "wishlist_tag",
                r => r.HasOne(typeof(Tag)).WithMany().HasForeignKey("TagName").HasPrincipalKey(nameof(Tag.Name)),
                l => l.HasOne(typeof(Wishlist)).WithMany()
                    .HasForeignKey("WishlistsId").HasPrincipalKey(nameof(Wishlist.Id)));
        builder.HasIndex(e => e.OwnerUserId);
        builder.HasIndex(e => e.ContentFilterId);
        builder.HasIndex(e => e.QualityTierListId);
        builder.HasIndex(e => e.RootFolderId);
    }
}
