using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for an indexer.
/// </summary>
public sealed record IndexerPatch
{
    /// <summary>
    /// The patch for the provider type of the indexer.
    /// </summary>
    public string? ProviderType { get; init; }

    /// <summary>
    /// The patch for the display name of the indexer.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// The patch for the description of the indexer.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// The patch for the availability of the indexer.
    /// </summary>
    public IndexerAvailability? Availability { get; init; }

    /// <summary>
    /// The patch for the categories that the indexer covers.
    /// </summary>
    public List<string>? Categories { get; init; }

    /// <summary>
    /// The patch for the languages that the indexer covers.
    /// </summary>
    public List<string>? Languages { get; init; }

    /// <summary>
    /// The patch for the indexer settings.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Settings { get; init; }
}
