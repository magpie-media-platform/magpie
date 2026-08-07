using System.Collections.Generic;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for an indexer feed.
/// </summary>
public sealed record IndexerFeedPatch
{
    /// <summary>
    /// The patch for the provider type of the feed.
    /// </summary>
    public string? ProviderType { get; init; }

    /// <summary>
    /// The patch for the display name of the feed.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// The patch for the description of the feed.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// The patch for the feed settings.
    /// </summary>
    public Dictionary<string, string>? Settings { get; init; }

    /// <summary>
    /// The patch for whether the feed is enabled.
    /// </summary>
    public bool? Enabled { get; init; }
}
