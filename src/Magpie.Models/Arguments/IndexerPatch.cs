using System.Collections.Generic;

using Magpie.Models.Data;
using Magpie.Models.Enums;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="Indexer"/>.
/// </summary>
public sealed record IndexerPatch
{
    /// <summary>
    /// The <see cref="Indexer.ProviderType"/> to patch.
    /// </summary>
    public string? ProviderType { get; init; }

    /// <summary>
    /// The <see cref="Indexer.Name"/> to patch.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// The <see cref="Indexer.Description"/> to patch.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// The <see cref="Indexer.Availability"/> to patch.
    /// </summary>
    public IndexerAvailability? Availability { get; init; }

    /// <summary>
    /// The <see cref="Indexer.Categories"/> to patch.
    /// </summary>
    public List<string>? Categories { get; init; }

    /// <summary>
    /// The <see cref="Indexer.Languages"/> to patch.
    /// </summary>
    public List<string>? Languages { get; init; }
}
