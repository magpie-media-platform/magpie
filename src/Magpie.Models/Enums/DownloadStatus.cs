namespace Magpie.Models.Enums;

/// <summary>
/// The status of a download.
/// </summary>
public enum DownloadStatus
{
    /// <summary>
    /// The download was grabbed and sent to the client.
    /// </summary>
    Grabbed = 0,

    /// <summary>
    /// The download is queued in the client.
    /// </summary>
    Queued,

    /// <summary>
    /// The download is actively downloading.
    /// </summary>
    Downloading,

    /// <summary>
    /// The download is paused.
    /// </summary>
    Paused,

    /// <summary>
    /// The download is stalled with no progress.
    /// </summary>
    Stalled,

    /// <summary>
    /// The download completed successfully.
    /// </summary>
    Downloaded,

    /// <summary>
    /// The download was imported into the library.
    /// </summary>
    Imported,

    /// <summary>
    /// The download failed.
    /// </summary>
    Failed
}
