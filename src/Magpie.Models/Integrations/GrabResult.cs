namespace Magpie.Models.Integrations;

/// <summary>
/// The result of a release grab.
/// </summary>
public sealed record GrabResult
{
    /// <summary>
    /// Whether the grab succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Whether the release was already tainted.
    /// </summary>
    public bool AlreadyTainted { get; set; }

    /// <summary>
    /// Whether the download client was unreachable.
    /// </summary>
    public bool ClientUnreachable { get; set; }

    /// <summary>
    /// The error message, when present.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The download information from the grab, when present.
    /// </summary>
    public DownloadInfo? DownloadInfo { get; set; }

    /// <summary>
    /// Creates a result for a successful grab.
    /// </summary>
    /// <param name="downloadInfo">The download information from the grab</param>
    /// <returns>The result</returns>
    public static GrabResult FromSuccess(DownloadInfo downloadInfo) => new()
    {
        Success = true,
        DownloadInfo = downloadInfo
    };

    /// <summary>
    /// Creates a result for an already-tainted release.
    /// </summary>
    /// <param name="errorMessage">The error message, when present</param>
    /// <returns>The result</returns>
    public static GrabResult FromAlreadyTainted(string? errorMessage = null) => new()
    {
        Success = false,
        AlreadyTainted = true,
        ErrorMessage = errorMessage
    };

    /// <summary>
    /// Creates a result for an unreachable download client.
    /// </summary>
    /// <param name="errorMessage">The error message, when present</param>
    /// <returns>The result</returns>
    public static GrabResult FromClientUnreachable(string? errorMessage = null) => new()
    {
        Success = false,
        ClientUnreachable = true,
        ErrorMessage = errorMessage
    };
}
