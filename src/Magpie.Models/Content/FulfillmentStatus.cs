namespace Magpie.Models.Content;

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
    /// The content item has media files and is open to upgrades.
    /// </summary>
    Upgradable,

    /// <summary>
    /// The content item has media files and is fully satisfied.
    /// </summary>
    Fulfilled,

    /// <summary>
    /// The content is owned externally and does not need to be acquired.
    /// </summary>
    OwnedExternally
}
