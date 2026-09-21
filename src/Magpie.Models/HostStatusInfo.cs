using System;

namespace Magpie.Models;

/// <summary>
/// The current state of the Magpie host process.
/// </summary>
public sealed class HostStatusInfo
{
    private readonly object _shutdownRequestLock = new();

    /// <summary>
    /// The kind of supervisor that manages the host.
    /// </summary>
    public HostMode Mode { get; set; }

    /// <summary>
    /// Whether a shutdown request is pending.
    /// </summary>
    public bool IsShutdownRequested { get; private set; }

    /// <summary>
    /// The id of the user that requested the shutdown.
    /// </summary>
    public Guid? ShutdownRequestedByUserId { get; private set; }

    /// <summary>
    /// The UTC time of the shutdown request.
    /// </summary>
    public DateTime? ShutdownRequestedAtUtc { get; private set; }

    /// <summary>
    /// The exit code that the host returns on the requested shutdown.
    /// </summary>
    public int? ShutdownExitCode { get; set; }

    /// <summary>
    /// Sets the pending shutdown request and the user and time that go with it in one step.
    /// </summary>
    /// <param name="requested">Whether a shutdown request is pending</param>
    /// <param name="userId">The id of the user that requested the shutdown</param>
    /// <param name="atUtc">The UTC time of the shutdown request</param>
    public void SetShutdownRequest(bool requested, Guid? userId, DateTime? atUtc)
    {
        lock (_shutdownRequestLock)
        {
            IsShutdownRequested = requested;
            ShutdownRequestedByUserId = userId;
            ShutdownRequestedAtUtc = atUtc;
        }
    }
}
