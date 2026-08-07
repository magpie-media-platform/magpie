namespace Magpie.Models.Enums;

/// <summary>
/// The status of a download.
/// </summary>
public enum DownloadStatus
{
    /// <summary>
    /// The grab is complete and the client has the download.
    /// </summary>
    Grabbed = 0,

    /// <summary>
    /// The download waits in the client queue.
    /// </summary>
    Queued = 1,

    /// <summary>
    /// The download is in progress.
    /// </summary>
    Downloading = 2,

    /// <summary>
    /// The download is paused.
    /// </summary>
    Paused = 3,

    /// <summary>
    /// The download makes no progress.
    /// </summary>
    Stalled = 4,

    /// <summary>
    /// The download is complete.
    /// </summary>
    Downloaded = 5,

    /// <summary>
    /// The library import of the download is complete.
    /// </summary>
    Imported = 6,

    /// <summary>
    /// A terminal failure stopped the grab, the download, or the import.
    /// </summary>
    Failed = 7,

    /// <summary>
    /// A user stopped the download.
    /// </summary>
    Cancelled = 8,

    /// <summary>
    /// The library import of the download is in progress.
    /// </summary>
    Importing = 9
}
