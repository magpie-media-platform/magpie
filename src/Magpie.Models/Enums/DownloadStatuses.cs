namespace Magpie.Models.Enums;

/// <summary>
/// The sets of download statuses.
/// </summary>
public static class DownloadStatuses
{
    /// <summary>
    /// The statuses of a download that a failure stopped.
    /// </summary>
    public static readonly DownloadStatus[] Failures = [DownloadStatus.Failed, DownloadStatus.Faulted];

    /// <summary>
    /// The final statuses of a download.
    /// </summary>
    public static readonly DownloadStatus[] Finished =
        [DownloadStatus.Imported, DownloadStatus.Failed, DownloadStatus.Faulted, DownloadStatus.Cancelled];
}
