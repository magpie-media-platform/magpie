namespace Magpie.Models.Enums;

/// <summary>
/// The in-library status of a discovered wishlist row.
/// </summary>
public enum WishlistDiscoveredItemStatus
{
    /// <summary>
    /// The content item exists but has no files.
    /// </summary>
    Missing = 0,

    /// <summary>
    /// The content item exists and has files.
    /// </summary>
    Found,
}
