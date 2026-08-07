namespace Magpie.Models.Data;

/// <summary>
/// The counts of discovered wishlist items by library presence.
/// </summary>
public sealed record WishlistDiscoveredCounts
{
    /// <summary>
    /// The number of discovered items.
    /// </summary>
    public required int TotalDiscovered { get; init; }

    /// <summary>
    /// The number of discovered items that the library holds.
    /// </summary>
    public required int InLibrary { get; init; }

    /// <summary>
    /// The number of discovered items that the library does not hold.
    /// </summary>
    public required int NotInLibrary { get; init; }

    /// <summary>
    /// The number of discovered items in the library with no file.
    /// </summary>
    public required int Missing { get; init; }
}
