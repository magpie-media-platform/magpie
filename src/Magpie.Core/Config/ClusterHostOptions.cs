using Magpie.Core.Extensions;

namespace Magpie.Core.Config;

/// <summary>
/// The cluster topology options for a Magpie host.
/// </summary>
public sealed record ClusterHostOptions
{
    /// <summary>
    /// The coordination mode for extensions across cluster nodes.
    /// </summary>
    public ExtensionClusterMode Mode { get; init; } = ExtensionClusterMode.SingleNode;

    /// <summary>
    /// The node identifier for cluster-visible heartbeats and job ownership.
    /// </summary>
    public string? NodeId { get; init; }
}
