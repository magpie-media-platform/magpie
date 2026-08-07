namespace Magpie.Models;

/// <summary>
/// The provider setting keys that download clients share.
/// </summary>
public static class DownloadClientSettingsKeys
{
    /// <summary>
    /// Whether cleanup waits for the client to release imported downloads.
    /// </summary>
    public const string HonorClientHoldPolicy = "honorClientHoldPolicy";
}
