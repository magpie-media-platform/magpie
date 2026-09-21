using System;
using System.Collections.Generic;

using Magpie.Models.Enums;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for an indexer.
/// </summary>
public sealed record IndexerPatch
{
    /// <summary>
    /// The id of the indexer to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The patch for the provider type of the indexer.
    /// </summary>
    public ScalarPatch<string>? ProviderType { get; init; }

    /// <summary>
    /// The patch for the display name of the indexer.
    /// </summary>
    public ScalarPatch<string>? Name { get; init; }

    /// <summary>
    /// The patch for the description of the indexer.
    /// </summary>
    public ScalarPatch<string?>? Description { get; init; }

    /// <summary>
    /// The patch for the availability of the indexer.
    /// </summary>
    public ScalarPatch<IndexerAvailability>? Availability { get; init; }

    /// <summary>
    /// The patch for the categories that the indexer covers.
    /// </summary>
    public ScalarPatch<List<string>>? Categories { get; init; }

    /// <summary>
    /// The patch for the languages that the indexer covers.
    /// </summary>
    public ScalarPatch<List<string>>? Languages { get; init; }
}
