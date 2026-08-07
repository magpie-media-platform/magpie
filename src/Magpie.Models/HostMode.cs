namespace Magpie.Models;

/// <summary>
/// The kind of supervisor that manages the Magpie host process.
/// </summary>
public enum HostMode
{
    /// <summary>
    /// No supervisor manages the host.
    /// </summary>
    Default = 0,
    /// <summary>
    /// The Magpie sentinel manages the host.
    /// </summary>
    Sentinel,
    /// <summary>
    /// A Windows service manages the host.
    /// </summary>
    WindowsService,
    /// <summary>
    /// A systemd unit manages the host.
    /// </summary>
    Systemd,
}
