using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Data;

/// <summary>
/// A terminal download that the cleanup job evaluates, with the details of its download client.
/// </summary>
public record DownloadCleanupCandidate
{
    /// <summary>
    /// The id of the download.
    /// </summary>
    public required Guid DownloadId { get; init; }

    /// <summary>
    /// The id of the download in the download client.
    /// </summary>
    public required string ExternalId { get; init; }

    /// <summary>
    /// The title of the download.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// The status of the download.
    /// </summary>
    public required DownloadStatus Status { get; init; }

    /// <summary>
    /// Whether the download client still holds the download.
    /// </summary>
    public required bool HeldByPolicy { get; init; }

    /// <summary>
    /// The provider type of the download client.
    /// </summary>
    public required string ProviderType { get; init; }

    /// <summary>
    /// The name of the download client.
    /// </summary>
    public required string ClientName { get; init; }

    /// <summary>
    /// The readable settings of the download client, or null if the settings cannot be read.
    /// </summary>
    public required IReadOnlyDictionary<string, string>? Settings { get; init; }

    /// <summary>
    /// Whether the cleanup deletes the files from the download client.
    /// </summary>
    public required bool CleanupDeleteFiles { get; init; }

    /// <summary>
    /// Whether failed downloads stay on the download client.
    /// </summary>
    public required bool KeepFailedOnClient { get; init; }
}
