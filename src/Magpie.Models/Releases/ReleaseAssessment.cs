using System;
using System.Collections.Generic;

namespace Magpie.Models.Releases;

/// <summary>
/// The match and quality assessment of one release against a content target.
/// </summary>
public sealed record ReleaseAssessment
{
    /// <summary>
    /// How well the release matches the content target.
    /// </summary>
    public required ReleaseMatchResult MatchResult { get; init; }

    /// <summary>
    /// The id of the quality tier that the release matches, or null when quality scoring did not run.
    /// </summary>
    public Guid? MatchedQualityTierId { get; init; }

    /// <summary>
    /// The rank ordinal of the matched quality tier, or null when quality scoring did not run.
    /// </summary>
    public int? MatchedRankOrdinal { get; init; }

    /// <summary>
    /// Whether the release quality is acceptable, or null when quality scoring did not run.
    /// </summary>
    public bool? IsAcceptable { get; init; }

    /// <summary>
    /// Whether the release quality is excluded, or null when quality scoring did not run.
    /// </summary>
    public bool? IsExcluded { get; init; }

    /// <summary>
    /// Whether the release meets the quality cutoff, or null when quality scoring did not run.
    /// </summary>
    public bool? IsCutoffMet { get; init; }

    /// <summary>
    /// The reasons for the quality score.
    /// </summary>
    public IReadOnlyList<string> QualityReasons { get; init; } = [];
}
