using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Arguments;

/// <summary>
/// The query filters for library import candidates.
/// </summary>
public sealed record LibraryImportCandidateQuery
{
    /// <summary>
    /// The id of the root folder to match.
    /// </summary>
    public Guid? RootFolderId { get; init; }

    /// <summary>
    /// The id of the scan run to match.
    /// </summary>
    public Guid? ScanRunId { get; init; }

    /// <summary>
    /// The candidate status to match.
    /// </summary>
    public LibraryImportCandidateStatus? Status { get; init; }

    /// <summary>
    /// The set of candidate statuses to match.
    /// </summary>
    public IReadOnlyCollection<LibraryImportCandidateStatus>? Statuses { get; init; }

    /// <summary>
    /// Whether a candidate has matches, where <see langword="false"/> selects the candidates with no match.
    /// </summary>
    public bool? HasMatches { get; init; }

    /// <summary>
    /// The ids of the candidates to exclude from the selection, or <c>null</c> to exclude nothing.
    /// </summary>
    public IReadOnlyList<Guid>? ExcludeIds { get; init; }
}
