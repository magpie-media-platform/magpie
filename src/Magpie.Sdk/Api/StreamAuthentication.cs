namespace Magpie.Sdk.Api;

/// <summary>
/// The constants for cookie authentication of the live-update stream.
/// </summary>
public static class StreamAuthentication
{
    /// <summary>
    /// The base name of the HttpOnly cookie that carries the access JWT for the live-update stream.
    /// </summary>
    public const string CookieName = "magpie.stream";

    /// <summary>
    /// The only route that accepts the stream cookie.
    /// </summary>
    public const string Path = "/api/events/stream";
}
