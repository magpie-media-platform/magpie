using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Data;

/// <summary>
/// A download of a release by a download client.
/// </summary>
public record Download
{
    /// <summary>
    /// The unique id of the download.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the release.
    /// </summary>
    public Guid? ReleaseId { get; set; }

    /// <summary>
    /// The id of the release source that was used for the grab.
    /// </summary>
    public Guid? ReleaseSourceId { get; set; }

    /// <summary>
    /// The id of the download client.
    /// </summary>
    public Guid DownloadClientId { get; set; }

    /// <summary>
    /// The id of the download in the download client.
    /// </summary>
    public required string ExternalId { get; set; }

    /// <summary>
    /// The title of the download.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// The status of the download.
    /// </summary>
    public DownloadStatus Status { get; set; }

    /// <summary>
    /// The explicit collision policy of the current import, or null to inherit the root-folder policy.
    /// </summary>
    public ImportCollisionMode? ImportCollisionMode { get; set; }

    /// <summary>
    /// The persisted file recovery state of an interrupted import, or null when no recovery is pending.
    /// </summary>
    public string? ImportRecoveryState { get; set; }

    /// <summary>
    /// The path of the downloaded files.
    /// </summary>
    public string? OutputPath { get; set; }

    /// <summary>
    /// The reason for the current status.
    /// </summary>
    public string? StatusReason { get; set; }

    /// <summary>
    /// The UTC time the download completed.
    /// </summary>
    public DateTime? DateCompleted { get; set; }

    /// <summary>
    /// The completion percent of the download.
    /// </summary>
    public decimal? Progress { get; set; }

    /// <summary>
    /// The UTC time of the last progress update.
    /// </summary>
    public DateTime? LastProgressAt { get; set; }

    /// <summary>
    /// The UTC time of the failure.
    /// </summary>
    public DateTime? FailedAt { get; set; }

    /// <summary>
    /// The reason for the failure.
    /// </summary>
    public DownloadFailureReason? FailureReason { get; set; }

    /// <summary>
    /// The UTC time the download client removed the download.
    /// </summary>
    public DateTime? ClientRemovedAt { get; set; }

    /// <summary>
    /// Whether the download client still holds the download.
    /// </summary>
    public bool HeldByPolicy { get; set; }

    /// <summary>
    /// The UTC time the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user who created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// The UTC time the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user who last modified the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }

    /// <summary>
    /// The related release.
    /// </summary>
    public Release? Release { get; set; }

    /// <summary>
    /// The release source that was used for the grab.
    /// </summary>
    public ReleaseSource? ReleaseSource { get; set; }

    /// <summary>
    /// The content hierarchy nodes in the scope of the download.
    /// </summary>
    public ICollection<DownloadScope> Scope { get; } = [];

    /// <summary>
    /// The snapshot of the release identity keys.
    /// </summary>
    public ICollection<ReleaseIdentityKey> ReleaseIdentityKeys { get; } = [];
}
