using System;

namespace Magpie.Models.Downloads;

/// <summary>
/// Ephemeral status of a download from the download client.
/// </summary>
/// <param name="Id">The client's identifier for this download.</param>
/// <param name="Name">The display name or title.</param>
/// <param name="State">The provider-defined state.</param>
/// <param name="Progress">The download progress (0-100).</param>
/// <param name="Size">The total size in bytes.</param>
/// <param name="Downloaded">The bytes downloaded so far.</param>
/// <param name="UploadSpeed">The upload speed in bytes per second.</param>
/// <param name="DownloadSpeed">The download speed in bytes per second.</param>
/// <param name="Eta">The estimated time remaining.</param>
/// <param name="ErrorMessage">The error message, if failed.</param>
/// <param name="OutputPath">The output path where files are or will be.</param>
public sealed record DownloadStatus(
    string Id,
    string Name,
    string State,
    double? Progress,
    long? Size,
    long? Downloaded,
    long? UploadSpeed,
    long? DownloadSpeed,
    TimeSpan? Eta,
    string? ErrorMessage,
    string? OutputPath);
