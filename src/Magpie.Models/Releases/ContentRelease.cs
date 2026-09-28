using System;
using System.Collections.Generic;

using Magpie.Models.Enums;
namespace Magpie.Models.Releases;

/// <summary>
/// One release row for a content item, with both its evaluation fields and its decision fields.
/// </summary>
public sealed record ContentRelease
{
    /// <summary>
    /// The id of the release.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// The title of the release.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// The id of the indexer that supplied the release.
    /// </summary>
    public Guid? IndexerId { get; init; }

    /// <summary>
    /// The name of the indexer that supplied the release.
    /// </summary>
    public string? IndexerName { get; init; }

    /// <summary>
    /// The protocol of the release.
    /// </summary>
    public required string Protocol { get; init; }

    /// <summary>
    /// The size of the release in bytes.
    /// </summary>
    public long? Size { get; init; }

    /// <summary>
    /// The publication date of the release.
    /// </summary>
    public DateTime PublishDate { get; init; }

    /// <summary>
    /// The URL of the release info page.
    /// </summary>
    public Uri? InfoUrl { get; init; }

    /// <summary>
    /// The identifier of the release at the provider.
    /// </summary>
    public string? ExternalId { get; init; }

    /// <summary>
    /// The categories of the release.
    /// </summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>
    /// The languages of the release.
    /// </summary>
    public IReadOnlyList<string> Languages { get; init; } = [];

    /// <summary>
    /// The provider-owned properties of the release.
    /// </summary>
    public string? Properties { get; init; }

    /// <summary>
    /// The score of the release.
    /// </summary>
    public int? Score { get; init; }

    /// <summary>
    /// Whether the release is acceptable.
    /// </summary>
    public bool? IsAcceptable { get; init; }

    /// <summary>
    /// Whether the release meets the quality cutoff.
    /// </summary>
    public bool? MeetsCutoff { get; init; }

    /// <summary>
    /// Whether the release matches the requested content.
    /// </summary>
    public bool? IsMatch { get; init; }

    /// <summary>
    /// The reason for the rejection of the release.
    /// </summary>
    public string? RejectionReason { get; init; }

    /// <summary>
    /// The acquisition status of the release.
    /// </summary>
    public DownloadStatus? AcquisitionStatus { get; init; }

    /// <summary>
    /// The reason that acquisition waits before a download starts.
    /// </summary>
    public string? AcquisitionWaitingReason { get; init; }

    /// <summary>
    /// The reason that the acquisition failed.
    /// </summary>
    public DownloadFailureReason? FailureReason { get; init; }

    /// <summary>
    /// The decision taken for this release, or <see langword="null"/> when it is only a candidate.
    /// </summary>
    public ReleaseHistoryDecisionKind? Decision { get; init; }

    /// <summary>
    /// The reason for the decision.
    /// </summary>
    public string? DecisionReason { get; init; }

    /// <summary>
    /// The UTC time of the grab, or <see langword="null"/> when the release was not grabbed.
    /// </summary>
    public DateTime? GrabbedAt { get; init; }

    /// <summary>
    /// The status of the release in the scope of the content item.
    /// </summary>
    public ReleaseScopeStatus Status { get; init; }
}
