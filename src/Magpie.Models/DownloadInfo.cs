using Magpie.Models.Enums;

namespace Magpie.Models;

/// <summary>
/// Information about a download from the download client.
/// </summary>
public record DownloadInfo
{
    /// <summary>
    /// The identifier from the download client.
    /// </summary>
    public required string ExternalId { get; set; }

    /// <summary>
    /// The status of the download.
    /// </summary>
    public DownloadStatus Status { get; set; }

    /// <summary>
    /// The reason for current status.
    /// </summary>
    public string? StatusReason { get; set; }

    /// <summary>
    /// The output path from the download client.
    /// </summary>
    public string? OutputPath { get; set; }
}
