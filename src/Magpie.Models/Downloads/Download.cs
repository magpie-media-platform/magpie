using System;

namespace Magpie.Models.Downloads;

/// <summary>
/// A grabbed download.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="ContentItemId">The content item this download is for.</param>
/// <param name="ReleaseId">The grabbed release, if known.</param>
/// <param name="DownloadClientId">The download client handling this download.</param>
/// <param name="ExternalId">The identifier from the download client.</param>
/// <param name="Title">The display title.</param>
/// <param name="State">The lifecycle state.</param>
/// <param name="OutputPath">The output path from the download client.</param>
/// <param name="FailureReason">The reason for failure, if failed.</param>
/// <param name="DateCompleted">The date the download completed.</param>
/// <param name="CreatedAt">When the record was created.</param>
/// <param name="CreatedByUserId">The user who created the record.</param>
/// <param name="ModifiedAt">When the record was last modified.</param>
/// <param name="ModifiedByUserId">The user who last modified the record.</param>
public sealed record Download(
    Guid Id,
    Guid? ContentItemId,
    Guid? ReleaseId,
    Guid DownloadClientId,
    string ExternalId,
    string Title,
    DownloadState State,
    string? OutputPath,
    string? FailureReason,
    DateTimeOffset? DateCompleted,
    DateTimeOffset CreatedAt,
    Guid? CreatedByUserId,
    DateTimeOffset? ModifiedAt,
    Guid? ModifiedByUserId);
