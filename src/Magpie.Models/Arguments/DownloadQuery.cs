using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Arguments;

/// <summary>
/// The query filters for downloads.
/// </summary>
public sealed record DownloadQuery
{
    /// <summary>
    /// The id of the release to match.
    /// </summary>
    public Guid? ReleaseId { get; init; }

    /// <summary>
    /// The ids of the releases to match.
    /// </summary>
    public IReadOnlyCollection<Guid>? ReleaseIds { get; init; }

    /// <summary>
    /// The id of the content hierarchy node to match, resolved through release coverage.
    /// </summary>
    public Guid? ContentItemId { get; init; }

    /// <summary>
    /// The ids of the content hierarchy nodes to match, resolved through download scope.
    /// </summary>
    public IReadOnlyCollection<Guid>? ContentHierarchyItemIds { get; init; }

    /// <summary>
    /// The id of the download client to match.
    /// </summary>
    public Guid? DownloadClientId { get; init; }

    /// <summary>
    /// The download status to match.
    /// </summary>
    public DownloadStatus? Status { get; init; }

    /// <summary>
    /// The set of download statuses to match.
    /// </summary>
    public IReadOnlyCollection<DownloadStatus>? Statuses { get; init; }

    /// <summary>
    /// Whether the download has retained import recovery state.
    /// </summary>
    public bool? HasImportRecoveryState { get; init; }

    /// <summary>
    /// The ids of the downloads to exclude from the results.
    /// </summary>
    public IReadOnlyCollection<Guid>? ExcludeIds { get; init; }

    /// <summary>
    /// The inclusive range of last changed times to match.
    /// </summary>
    public Range<DateTime>? LastChangedAt { get; init; }

    /// <summary>
    /// The range of completion times to match.
    /// </summary>
    public Range<DateTime>? DateCompleted { get; init; }
}
