using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;

namespace Magpie.Sdk.Services;

/// <summary>
/// The contract for the running Magpie host.
/// </summary>
public interface IMagpieSystem
{
    /// <summary>
    /// Whether the host runs as one node of a cluster.
    /// </summary>
    bool IsClustered { get; }

    /// <summary>
    /// The id of the local node.
    /// </summary>
    string NodeId { get; }

    /// <summary>
    /// Gets the build, runtime, and node information of the host, with its restart capability.
    /// </summary>
    SystemInfo GetInfo();

    /// <summary>
    /// Gets the liveness report of the host.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<SystemHealth> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests a graceful shutdown of the host.
    /// </summary>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task RequestShutdownAsync(Guid? actingUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests a graceful restart of the host.
    /// </summary>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task RequestRestartAsync(Guid? actingUserId, CancellationToken cancellationToken = default);
}
