namespace Magpie.Abstractions;

/// <summary>
/// The exit codes that the Magpie host returns.
/// </summary>
public static class MagpieExitCodes
{
    /// <summary>
    /// The exit code for a requested shutdown.
    /// </summary>
    public const int Shutdown = 8;

    /// <summary>
    /// The exit code for a requested restart.
    /// </summary>
    public const int Restart = 9;

    /// <summary>
    /// The exit code for a startup failure or an unrequested stop.
    /// </summary>
    public const int StartupFailed = 1;
}
