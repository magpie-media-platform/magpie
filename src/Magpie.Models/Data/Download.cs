using System;

using Magpie.Models.Enums;

namespace Magpie.Models.Data;

/// <summary>
/// A grabbed download.
/// </summary>
public record Download
{
    /// <summary>
    /// The download id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The grabbed release, if known.
    /// </summary>
    public Guid? ReleaseId { get; set; }

    /// <summary>
    /// The download client handling this download.
    /// </summary>
    public Guid DownloadClientId { get; set; }

    /// <summary>
    /// The identifier from the download client.
    /// </summary>
    public required string ExternalId { get; set; }

    /// <summary>
    /// The display title.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// The status of the download.
    /// </summary>
    public DownloadStatus Status { get; set; }

    /// <summary>
    /// The output path from the download client.
    /// </summary>
    public string? OutputPath { get; set; }

    /// <summary>
    /// The reason for current status.
    /// </summary>
    public string? StatusReason { get; set; }

    /// <summary>
    /// The date the download completed.
    /// </summary>
    public DateTime? DateCompleted { get; set; }

    /// <summary>
    /// When the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user that created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// When the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user that last modified the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }

    /// <summary>
    /// The <see cref="Release"/> that was grabbed.
    /// </summary>
    public Release? Release { get; set; }


}
