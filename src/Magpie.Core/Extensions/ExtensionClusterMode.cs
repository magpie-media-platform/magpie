namespace Magpie.Core.Extensions;

/// <summary>
/// The extension loading topology of a Magpie host across cluster nodes.
/// </summary>
public enum ExtensionClusterMode
{
    /// <summary>
    /// One standalone node that loads extensions from the configured path.
    /// </summary>
    SingleNode = 0,

    /// <summary>
    /// Multiple nodes that load extensions from one shared path.
    /// </summary>
    MultiNodeSharedExtensions = 1,

    /// <summary>
    /// Multiple nodes that load extensions from a node-local path.
    /// </summary>
    MultiNodeLocalExtensions = 2
}
