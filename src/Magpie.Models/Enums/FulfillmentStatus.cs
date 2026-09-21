namespace Magpie.Models.Enums;

/// <summary>
/// The fulfillment status of a content item.
/// </summary>
public enum FulfillmentStatus
{
    /// <summary>
    /// The content item has no media files.
    /// </summary>
    Missing = 0,

    /// <summary>
    /// The content item has media files and accepts upgrades.
    /// </summary>
    Upgradable = 1,

    /// <summary>
    /// The content item has media files and needs no more.
    /// </summary>
    Fulfilled = 2,

    /// <summary>
    /// An external source fulfills the content item, so it needs no acquisition.
    /// </summary>
    FulfilledExternally = 3,

    /// <summary>
    /// A container with some, but not all, of its descendant leaves covered.
    /// </summary>
    Partial = 4
}
