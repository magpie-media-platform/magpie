using System;
using System.Collections.Generic;

namespace Magpie.Models.Api;

/// <summary>
/// The quality score of a release in an interactive search.
/// </summary>
public sealed record InteractiveSearchCandidateScore
{
    /// <summary>
    /// The id of the scored release.
    /// </summary>
    public required Guid ReleaseId { get; init; }

    /// <summary>
    /// The id of the quality tier list used for the score.
    /// </summary>
    public required Guid QualityTierListId { get; init; }

    /// <summary>
    /// The id of the quality tier that matched, when a tier matched.
    /// </summary>
    public Guid? MatchedQualityTierId { get; init; }

    /// <summary>
    /// The rank ordinal of the tier that matched, when a tier matched.
    /// </summary>
    public int? MatchedRankOrdinal { get; init; }

    /// <summary>
    /// Whether the release is acceptable.
    /// </summary>
    public bool IsAcceptable { get; init; }

    /// <summary>
    /// Whether the tier list excludes the release.
    /// </summary>
    public bool IsExcluded { get; init; }

    /// <summary>
    /// Whether the release meets the quality cutoff.
    /// </summary>
    public bool IsCutoffMet { get; init; }

    /// <summary>
    /// The reasons for the score.
    /// </summary>
    public IReadOnlyList<string> Reasons { get; init; } = [];
}
