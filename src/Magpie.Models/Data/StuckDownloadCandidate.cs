using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Data;

/// <summary>
/// A download that is over the stuck-detection limits, with the details of its download client.
/// </summary>
public record StuckDownloadCandidate
{
    /// <summary>
    /// The id of the stuck download.
    /// </summary>
    public required Guid DownloadId { get; init; }

    /// <summary>
    /// The id of the download at the download client.
    /// </summary>
    public required string ExternalId { get; init; }

    /// <summary>
    /// The title of the download to show.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// The last known state of the download, from <see cref="DownloadStatus"/>.
    /// </summary>
    public required DownloadStatus Status { get; init; }

    /// <summary>
    /// The progress of the download, as the download client reports it.
    /// </summary>
    public decimal? Progress { get; init; }

    /// <summary>
    /// The UTC time of the last progress change.
    /// </summary>
    public DateTime? LastProgressAt { get; init; }

    /// <summary>
    /// The UTC time from which the inactivity period counts.
    /// </summary>
    public required DateTime InactivityAnchor { get; init; }

    /// <summary>
    /// The UTC time when the download finished.
    /// </summary>
    public DateTime? DateCompleted { get; init; }

    /// <summary>
    /// The reason for the failure, from <see cref="DownloadFailureReason"/>.
    /// </summary>
    public DownloadFailureReason? FailureReason { get; init; }

    /// <summary>
    /// The UTC time when the download failed.
    /// </summary>
    public DateTime? FailedAt { get; init; }

    /// <summary>
    /// The id of the release that the download grabbed.
    /// </summary>
    public Guid? ReleaseId { get; init; }

    /// <summary>
    /// The name of the provider type of the download client.
    /// </summary>
    public required string ProviderType { get; init; }

    /// <summary>
    /// The name of the download client.
    /// </summary>
    public required string ClientName { get; init; }

    /// <summary>
    /// The settings of the download client, keyed by name.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Settings { get; init; }
}
