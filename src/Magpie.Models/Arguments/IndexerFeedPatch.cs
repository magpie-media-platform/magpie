using System.Collections.Generic;

using Magpie.Models.Data;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="IndexerFeed"/>.
/// </summary>
public sealed record IndexerFeedPatch
{
    /// <summary>
    /// The <see cref="IndexerFeed.ProviderType"/> to patch.
    /// </summary>
    public string? ProviderType { get; init; }

    /// <summary>
    /// The <see cref="IndexerFeed.Name"/> to patch.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// The <see cref="IndexerFeed.Description"/> to patch.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// The <see cref="IndexerFeed.Settings"/> to patch.
    /// </summary>
    public Dictionary<string, string>? Settings { get; init; }

    /// <summary>
    /// The <see cref="IndexerFeed.Enabled"/> to patch.
    /// </summary>
    public bool? Enabled { get; init; }
}
