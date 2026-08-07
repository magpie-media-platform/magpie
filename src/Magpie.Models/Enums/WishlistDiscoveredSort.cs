namespace Magpie.Models.Enums;

/// <summary>
/// The sort field for the discovered log of a wishlist.
/// </summary>
public enum WishlistDiscoveredSort
{
    /// <summary>
    /// The date of discovery.
    /// </summary>
    Discovered = 0,

    /// <summary>
    /// The date the content item entered the library.
    /// </summary>
    Added,

    /// <summary>
    /// The publish date from the metadata.
    /// </summary>
    PublishDate,

    /// <summary>
    /// The title snapshot.
    /// </summary>
    Title,

    /// <summary>
    /// The in-library status.
    /// </summary>
    Status,
}
