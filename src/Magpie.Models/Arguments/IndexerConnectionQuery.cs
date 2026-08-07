using System;

namespace Magpie.Models.Arguments;

/// <summary>
/// The query filters for indexer connections.
/// </summary>
public sealed record IndexerConnectionQuery
{
    /// <summary>
    /// The id of the indexer to match.
    /// </summary>
    public Guid? IndexerId { get; init; }

    /// <summary>
    /// The <see cref="Data.Tag"/> name that a connection must carry.
    /// </summary>
    public string? Tag { get; init; }
}
