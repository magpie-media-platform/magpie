using System.Collections.Generic;

using Magpie.Models.Data;
using Magpie.Models.Enums;

namespace Magpie.Models.Import;

/// <summary>
/// The aggregate of one library import candidate group, as one page of a group query returns it.
/// </summary>
public sealed record LibraryImportCandidateGroupSummary
{
    /// <summary>
    /// The key that every candidate of the group holds.
    /// </summary>
    public required string GroupKey { get; init; }

    /// <summary>
    /// One candidate of the group, preferring a candidate with exactly one match.
    /// </summary>
    public required LibraryImportCandidate Representative { get; init; }

    /// <summary>
    /// The number of candidates in the group at each status.
    /// </summary>
    public required IReadOnlyDictionary<LibraryImportCandidateStatus, int> StatusCounts { get; init; }
}
