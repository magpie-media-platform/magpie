using System;

using Magpie.Models.Enums;

namespace Magpie.Models.Data;

/// <summary>
/// A download queue entry with its client, content, and release context.
/// </summary>
public record DownloadQueueEntry
{
    /// <summary>
    /// The unique id of the download queue entry.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The id of the release.
    /// </summary>
    public Guid? ReleaseId { get; init; }

    /// <summary>
    /// The id of the download client.
    /// </summary>
    public Guid DownloadClientId { get; init; }

    /// <summary>
    /// The display name of the download client.
    /// </summary>
    public string? DownloadClientName { get; init; }

    /// <summary>
    /// The provider type of the download client.
    /// </summary>
    public string? ProviderType { get; init; }

    /// <summary>
    /// The id of the download in the download client.
    /// </summary>
    public required string ExternalId { get; init; }

    /// <summary>
    /// The title of the download.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// The content hierarchy name.
    /// </summary>
    public string? ContentHierarchyName { get; init; }

    /// <summary>
    /// The content metadata title.
    /// </summary>
    public string? ContentMetadataTitle { get; init; }

    /// <summary>
    /// The id of the content hierarchy item.
    /// </summary>
    public Guid? ContentHierarchyItemId { get; init; }

    /// <summary>
    /// The id of the parent content hierarchy item.
    /// </summary>
    public Guid? ContentHierarchyParentId { get; init; }

    /// <summary>
    /// The URL of the poster image.
    /// </summary>
    public string? PosterUrl { get; init; }

    /// <summary>
    /// The URL of the release information page.
    /// </summary>
    public string? ReleaseInfoUrl { get; init; }

    /// <summary>
    /// The status of the download.
    /// </summary>
    public DownloadStatus Status { get; init; }

    /// <summary>
    /// The path of the downloaded files.
    /// </summary>
    public string? OutputPath { get; init; }

    /// <summary>
    /// The reason for the current status.
    /// </summary>
    public string? StatusReason { get; init; }

    /// <summary>
    /// The UTC time the download completed.
    /// </summary>
    public DateTime? DateCompleted { get; init; }

    /// <summary>
    /// The completion percent of the download.
    /// </summary>
    public decimal? Progress { get; init; }

    /// <summary>
    /// The UTC time of the last progress update.
    /// </summary>
    public DateTime? LastProgressAt { get; init; }

    /// <summary>
    /// The UTC time of the failure.
    /// </summary>
    public DateTime? FailedAt { get; init; }

    /// <summary>
    /// The reason for the failure.
    /// </summary>
    public DownloadFailureReason? FailureReason { get; init; }

    /// <summary>
    /// The UTC time the record was created.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// The UTC time the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; init; }

    /// <summary>
    /// Whether the download client can pause the download.
    /// </summary>
    public bool CanPause { get; init; }

    /// <summary>
    /// Whether the download client can resume the download.
    /// </summary>
    public bool CanResume { get; init; }

    /// <summary>
    /// Whether the download client can force the start of the download.
    /// </summary>
    public bool CanForceStart { get; init; }

    /// <summary>
    /// The UTC time the download client removed the download.
    /// </summary>
    public DateTime? ClientRemovedAt { get; init; }

    /// <summary>
    /// Whether the download client still holds the download.
    /// </summary>
    public bool HeldByPolicy { get; init; }
}
