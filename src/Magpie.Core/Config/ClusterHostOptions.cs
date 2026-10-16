using System.Collections.Generic;

using Magpie.Core.Extensions;
using Magpie.Models.Enums;

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

    /// <summary>
    /// The roles that the node serves.
    /// </summary>
    public IReadOnlyList<NodeRole> Roles { get; init; } = [];
}
