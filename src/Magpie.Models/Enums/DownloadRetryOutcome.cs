namespace Magpie.Models.Enums;

/// <summary>
/// The result of a request to retry a download.
/// </summary>
public enum DownloadRetryOutcome
{
    /// <summary>
    /// No download with the given id exists.
    /// </summary>
    NotFound,

    /// <summary>
    /// The download is in a state that does not permit a retry.
    /// </summary>
    Conflict,

    /// <summary>
    /// The download was retried.
    /// </summary>
    Retried,
}
