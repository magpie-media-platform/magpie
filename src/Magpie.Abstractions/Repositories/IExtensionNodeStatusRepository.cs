using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for the extension activation heartbeat of each node.
/// </summary>
public interface IExtensionNodeStatusRepository : IRepository<ExtensionNodeStatus>
{
    /// <summary>
    /// Gets the status of one node by node id.
    /// </summary>
    /// <param name="nodeId">The id of the node to look up</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The status of the node, or <c>null</c> when the node is unknown</returns>
    Task<ExtensionNodeStatus?> GetByNodeIdAsync(string nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a node status row, or updates the row when it exists already.
    /// </summary>
    /// <param name="status">The node status to store</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The stored node status</returns>
    Task<ExtensionNodeStatus> UpsertAsync(
        ExtensionNodeStatus status,
        CancellationToken cancellationToken = default);
}
