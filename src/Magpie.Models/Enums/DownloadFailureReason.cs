namespace Magpie.Models.Enums;

/// <summary>
/// The reason a download moved to <see cref="DownloadStatus.Failed"/> or <see cref="DownloadStatus.Faulted"/>.
/// </summary>
public enum DownloadFailureReason
{
    /// <summary>
    /// The import finished with no accepted file.
    /// </summary>
    NoUsableContent = 2,

    /// <summary>
    /// The download made no progress for longer than the stuck-download threshold.
    /// </summary>
    DownloadStuck = 3,

    /// <summary>
    /// The download client reported a terminal failure status for the item.
    /// </summary>
    DownloadErrored = 4,

    /// <summary>
    /// The import failed after the download finished.
    /// </summary>
    ImportFailed = 5,

    /// <summary>
    /// No enabled download client can take the release.
    /// </summary>
    NoCompatibleClient = 6,

    /// <summary>
    /// Every eligible download client rejected the grab.
    /// </summary>
    ClientRejected = 7,

    /// <summary>
    /// The download contained a file with a blocked extension or MIME type.
    /// </summary>
    BlockedFileType = 8,

    /// <summary>
    /// A grab failed after a possible dispatch to a client, so the result is not known.
    /// </summary>
    AmbiguousDispatchFailure = 10,

    /// <summary>
    /// The release size exceeds the available space at the destination.
    /// </summary>
    InsufficientDestinationSpace = 11
}
