using System;
using System.Collections.Generic;

namespace Magpie.Models.Arguments;

/// <summary>
/// The query filters for releases.
/// </summary>
public sealed record ReleaseQuery
{
    /// <summary>
    /// The release ids to match.
    /// </summary>
    public IReadOnlyCollection<Guid>? Ids { get; init; }

    /// <summary>
    /// The id of the indexer to match.
    /// </summary>
    public Guid? IndexerId { get; init; }

    /// <summary>
    /// The id of the content hierarchy node to match, resolved through release scope.
    /// </summary>
    public Guid? ContentHierarchyItemId { get; init; }

    /// <summary>
    /// The ids of the content hierarchy nodes to match, resolved through release scope.
    /// </summary>
    public IReadOnlyCollection<Guid>? ContentHierarchyItemIds { get; init; }

    /// <summary>
    /// The release title to match.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// The release category to match.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// The external id to match.
    /// </summary>
    public string? ExternalId { get; init; }

    /// <summary>
    /// The range of publish dates to match.
    /// </summary>
    public Range<DateTime>? PublishDate { get; init; }

    /// <summary>
    /// The range of observation times to match.
    /// </summary>
    public Range<DateTime>? ObservedAt { get; init; }

    /// <summary>
    /// The tags to match.
    /// </summary>
    public IEnumerable<string>? Tags { get; init; }
}
