namespace Magpie.Sdk.Releases;

/// <summary>
/// The status of an interactive release grab.
/// </summary>
public enum InteractiveReleaseGrabResult
{
    /// <summary>
    /// The acquisition is queued.
    /// </summary>
    Accepted,

    /// <summary>
    /// The release request is invalid.
    /// </summary>
    InvalidRelease,

    /// <summary>
    /// The content does not exist.
    /// </summary>
    ContentNotFound,

    /// <summary>
    /// No download client is enabled.
    /// </summary>
    NoDownloadClient,
}
