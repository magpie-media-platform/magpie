namespace Magpie.Models.Enums;

/// <summary>
/// The extension activation health of a node.
/// </summary>
public enum ExtensionNodeHealth
{
    /// <summary>
    /// The node runs the current extension activation generation.
    /// </summary>
    CurrentHealthy = 0,

    /// <summary>
    /// The node is behind the cluster activation generation and needs a restart.
    /// </summary>
    StaleRequiresRestart = 1,

    /// <summary>
    /// The node failed to apply the extension activation state.
    /// </summary>
    ActivationFailed = 2,

    /// <summary>
    /// The node has no current heartbeat.
    /// </summary>
    Offline = 3
}
