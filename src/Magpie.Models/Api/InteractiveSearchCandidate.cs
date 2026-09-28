using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Releases;

namespace Magpie.Models.Api;

/// <summary>
/// A candidate release in the results of an interactive search.
/// </summary>
/// <param name="Release">The release offered as a candidate</param>
/// <param name="MatchResult">The strength of the match between the release and the request</param>
/// <param name="Score">The quality score of the release, when a score is available</param>
/// <param name="IndexerName">The name of the indexer that supplied the release</param>
public sealed record InteractiveSearchCandidate(
    Release Release,
    ReleaseMatchResult MatchResult,
    InteractiveSearchCandidateScore? Score,
    string? IndexerName = null)
{
    /// <summary>
    /// The acquisition status of the release, or null when none exists.
    /// </summary>
    public DownloadStatus? AcquisitionStatus { get; init; }

    /// <summary>
    /// The reason that the acquisition failed.
    /// </summary>
    public DownloadFailureReason? FailureReason { get; init; }
}
