using System;

namespace Magpie.Models;

/// <summary>
/// The current state of the Magpie host process.
/// </summary>
public sealed class HostStatusInfo
{
    /// <summary>
    /// The kind of supervisor that manages the host.
    /// </summary>
    public HostMode Mode { get; set; }
    /// <summary>
    /// Whether a shutdown request is pending.
    /// </summary>
    public bool IsShutdownRequested { get; set; }
    /// <summary>
    /// The id of the user that requested the shutdown.
    /// </summary>
    public Guid? ShutdownRequestedByUserId { get; set; }
    /// <summary>
    /// The UTC time of the shutdown request.
    /// </summary>
    public DateTime? ShutdownRequestedAtUtc { get; set; }
    /// <summary>
    /// The exit code that the host returns on the requested shutdown.
    /// </summary>
    public int? ShutdownExitCode { get; set; }
}
