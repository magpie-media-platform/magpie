using System;
using System.Collections.Generic;

using Magpie.Models.Data;
namespace Magpie.Models.Releases;

/// <summary>
/// The resolved facts that match and score releases for one content hierarchy item.
/// </summary>
public sealed record ReleaseEvaluationContext
{
    /// <summary>
    /// The id of the content hierarchy item that the context describes.
    /// </summary>
    public required Guid ContentHierarchyItemId { get; init; }

    /// <summary>
    /// The content target that a release must match.
    /// </summary>
    public ContentTarget? Target { get; init; }

    /// <summary>
    /// The facts for the target.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<ContentFact>> ContentFacts { get; init; } = [];

    /// <summary>
    /// The id of the quality tier list for the target.
    /// </summary>
    public Guid? QualityTierListId { get; init; }

    /// <summary>
    /// The quality tier list for the target.
    /// </summary>
    public QualityTierList? QualityTierList { get; init; }

    /// <summary>
    /// The quality tier rank of the file on disk.
    /// </summary>
    public int? CurrentFileQualityRank { get; init; }

    /// <summary>
    /// The release group of the file on disk.
    /// </summary>
    public string? CurrentFileGroup { get; init; }

    /// <summary>
    /// The revision markers of the file on disk.
    /// </summary>
    public ReleaseRevision? CurrentFileRevision { get; init; }
}
