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
    public required Guid Id { get; set; }

    /// <summary>
    /// The title of the release.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// The id of the indexer that supplied the release.
    /// </summary>
    public Guid? IndexerId { get; set; }

    /// <summary>
    /// The name of the indexer that supplied the release.
    /// </summary>
    public string? IndexerName { get; set; }

    /// <summary>
    /// The protocol of the release.
    /// </summary>
    public required string Protocol { get; set; }

    /// <summary>
    /// The size of the release in bytes.
    /// </summary>
    public long? Size { get; set; }

    /// <summary>
    /// The publication date of the release.
    /// </summary>
    public DateTime PublishDate { get; set; }

    /// <summary>
    /// The URL of the release info page.
    /// </summary>
    public Uri? InfoUrl { get; set; }

    /// <summary>
    /// The identifier of the release at the provider.
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// The categories of the release.
    /// </summary>
    public IReadOnlyList<string> Categories { get; set; } = [];

    /// <summary>
    /// The languages of the release.
    /// </summary>
    public IReadOnlyList<string> Languages { get; set; } = [];

    /// <summary>
    /// The provider-owned properties of the release.
    /// </summary>
    public string? Properties { get; set; }

    /// <summary>
    /// The score of the release.
    /// </summary>
    public int? Score { get; set; }

    /// <summary>
    /// Whether the release is acceptable.
    /// </summary>
    public bool? IsAcceptable { get; set; }

    /// <summary>
    /// Whether the release meets the quality cutoff.
    /// </summary>
    public bool? MeetsCutoff { get; set; }

    /// <summary>
    /// Whether the release matches the requested content.
    /// </summary>
    public bool? IsMatch { get; set; }

    /// <summary>
    /// The reason for the rejection of the release.
    /// </summary>
    public string? RejectionReason { get; set; }

    /// <summary>
    /// The acquisition status of the release.
    /// </summary>
    public DownloadStatus? AcquisitionStatus { get; set; }

    /// <summary>
    /// The reason that the acquisition failed.
    /// </summary>
    public DownloadFailureReason? FailureReason { get; set; }

    /// <summary>
    /// The decision taken for this release, or <see langword="null"/> when it is only a candidate.
    /// </summary>
    public ReleaseHistoryDecisionKind? Decision { get; set; }

    /// <summary>
    /// The reason for the decision.
    /// </summary>
    public string? DecisionReason { get; set; }

    /// <summary>
    /// The UTC time of the grab, or <see langword="null"/> when the release was not grabbed.
    /// </summary>
    public DateTime? GrabbedAt { get; set; }
}
