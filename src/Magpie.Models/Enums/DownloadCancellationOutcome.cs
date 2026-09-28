namespace Magpie.Models.Enums;

/// <summary>
/// The outcome of a request to cancel a download.
/// </summary>
public enum DownloadCancellationOutcome
{
    /// <summary>
    /// The client transfer stopped and the attempt remains in history.
    /// </summary>
    Cancelled,

    /// <summary>
    /// The attempt was already cancelled.
    /// </summary>
    AlreadyCancelled,

    /// <summary>
    /// The download record does not exist.
    /// </summary>
    NotFound,

    /// <summary>
    /// The download status does not permit cancellation.
    /// </summary>
    NotCancellable,

    /// <summary>
    /// The client or its download identifier is not available.
    /// </summary>
    ClientUnavailable,

    /// <summary>
    /// The client request failed and cancellation can be retried.
    /// </summary>
    ClientError
}
