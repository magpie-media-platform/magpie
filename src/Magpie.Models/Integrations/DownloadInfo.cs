using Magpie.Models.Enums;
namespace Magpie.Models.Integrations;

/// <summary>
/// The state of one download in the download client.
/// </summary>
public record DownloadInfo
{
    /// <summary>
    /// The external identifier of this download.
    /// </summary>
    public required string ExternalId { get; set; }

    /// <summary>
    /// The status of the download.
    /// </summary>
    public DownloadStatus Status { get; set; }

    /// <summary>
    /// The reason for the status, when present.
    /// </summary>
    public string? StatusReason { get; set; }

    /// <summary>
    /// The output path of the download, when present.
    /// </summary>
    public string? OutputPath { get; set; }

    /// <summary>
    /// The progress of the download in percent.
    /// </summary>
    public decimal? ProgressPercent { get; set; }

    /// <summary>
    /// Whether a client policy holds the download.
    /// </summary>
    public bool HeldByPolicy { get; set; }
}
