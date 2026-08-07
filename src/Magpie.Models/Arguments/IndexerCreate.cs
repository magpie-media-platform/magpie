using System.Collections.Generic;

using Magpie.Models.Data;
using Magpie.Models.Enums;

namespace Magpie.Models.Arguments;

/// <summary>
/// The create arguments for an <see cref="Indexer"/>.
/// </summary>
public sealed record IndexerCreate
{
    /// <summary>
    /// The provider type of the indexer.
    /// </summary>
    public required string ProviderType { get; init; }

    /// <summary>
    /// The display name of the indexer.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The description of the indexer.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// The availability of the indexer.
    /// </summary>
    public IndexerAvailability Availability { get; init; }

    /// <summary>
    /// The categories that the indexer covers.
    /// </summary>
    public List<string>? Categories { get; init; }

    /// <summary>
    /// The languages that the indexer covers.
    /// </summary>
    public List<string>? Languages { get; init; }

    /// <summary>
    /// The indexer settings.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Settings { get; init; }
}
