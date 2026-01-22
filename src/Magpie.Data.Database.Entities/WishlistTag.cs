using System;

namespace Magpie.Data.Database.Entities;

public record WishlistTag
{
    public Guid WishlistId { get; set; }
    public required string TagName { get; set; }

    public Wishlist? Wishlist { get; set; }
    public Tag? Tag { get; set; }
}
