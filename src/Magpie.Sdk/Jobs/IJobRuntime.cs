using System;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Sdk.Jobs;

/// <summary>
/// The runtime contract for job executions on a node.
/// </summary>
public interface IJobRuntime : IDisposable
{
    /// <summary>
    /// The name of the runtime implementation.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Initializes the runtime for a node.
    /// </summary>
    /// <param name="nodeId">The id of the node that claims and owns executions</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task InitializeAsync(string nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a job execution with distributed ownership.
    /// </summary>
    /// <param name="executionId">The id of the job execution to run</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task ExecuteAsync(Guid executionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a job execution across the cluster.
    /// </summary>
    /// <param name="executionId">The id of the job execution to cancel</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> for an accepted cancellation, otherwise <see langword="false"/>.</returns>
    Task<bool> TryCancelAsync(Guid executionId, CancellationToken cancellationToken = default);
}
