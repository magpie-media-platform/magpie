using System;

using Magpie.Models.Enums;

namespace Magpie.Models.Data;

/// <summary>
/// The heartbeat of one node for the extension activation state.
/// </summary>
public record ExtensionNodeStatus
{
    /// <summary>
    /// The id of the node, from the host configuration.
    /// </summary>
    public required string NodeId { get; set; }

    /// <summary>
    /// The activation generation of the cluster that this node applied.
    /// </summary>
    public long AppliedGeneration { get; set; }

    /// <summary>
    /// The extension activation health of the node.
    /// </summary>
    public ExtensionNodeHealth ExtensionHealth { get; set; }

    /// <summary>
    /// Whether the node can start or dequeue new jobs.
    /// </summary>
    public bool SchedulerEligible { get; set; }

    /// <summary>
    /// The UTC time of the last heartbeat.
    /// </summary>
    public DateTime LastSeenAt { get; set; }

    /// <summary>
    /// The status message for operators.
    /// </summary>
    public string? Message { get; set; }
}
