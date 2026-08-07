using System;

namespace Magpie.Models.Arguments;

/// <summary>
/// The query filters for indexers.
/// </summary>
public sealed record IndexerQuery
{
    /// <summary>
    /// The id of the feed to match.
    /// </summary>
    public Guid? FeedId { get; init; }
}
