using System;

namespace Magpie.Data.Database.Entities;

public class WishlistTag
{
    public Guid WishlistId { get; set; }
    public required string TagName { get; set; }

    public required Wishlist Wishlist { get; set; }
    public required Tag Tag { get; set; }
}
