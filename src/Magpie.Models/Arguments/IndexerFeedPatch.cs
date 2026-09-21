using System;

using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for an indexer feed.
/// </summary>
public sealed record IndexerFeedPatch
{
    /// <summary>
    /// The id of the indexer feed to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The patch for the provider type of the feed.
    /// </summary>
    public ScalarPatch<string>? ProviderType { get; init; }

    /// <summary>
    /// The patch for the display name of the feed.
    /// </summary>
    public ScalarPatch<string>? Name { get; init; }

    /// <summary>
    /// The patch for the description of the feed.
    /// </summary>
    public ScalarPatch<string?>? Description { get; init; }

    /// <summary>
    /// The patch for the feed settings.
    /// </summary>
    public DictionaryPatch<string, string, string>? Settings { get; init; }

    /// <summary>
    /// The patch for whether the feed is enabled.
    /// </summary>
    public ScalarPatch<bool>? Enabled { get; init; }
}
