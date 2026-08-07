namespace Magpie.Models.Data;

/// <summary>
/// Aggregate fulfillment counts for the content library.
/// </summary>
public sealed record ContentStats
{
    /// <summary>
    /// The total number of content hierarchy items in the library.
    /// </summary>
    public int Total { get; init; }

    /// <summary>
    /// The number of items that are missing (no content item, or fulfillment is missing).
    /// </summary>
    public int Missing { get; init; }

    /// <summary>
    /// The number of items that have content but are open to upgrades.
    /// </summary>
    public int Upgradable { get; init; }

    /// <summary>
    /// The number of items that are fully satisfied locally.
    /// </summary>
    public int Fulfilled { get; init; }

    /// <summary>
    /// The number of items fulfilled by an external source.
    /// </summary>
    public int FulfilledExternally { get; init; }

    /// <summary>
    /// The number of containers with some but not all descendant leaves covered.
    /// </summary>
    public int Partial { get; init; }
}
