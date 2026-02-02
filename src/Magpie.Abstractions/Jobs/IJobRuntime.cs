using System;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Abstractions.Jobs;

/// <summary>
/// Executes job executions.
/// </summary>
public interface IJobRuntime : IDisposable
{
    /// <summary>
    /// The name of this runtime implementation.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Initialize the runtime.
    /// </summary>
    /// <param name="nodeId">The node identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task InitializeAsync(string nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a job.
    /// </summary>
    /// <param name="executionId">The job execution id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task ExecuteAsync(Guid executionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Request cancellation of a running job.
    /// </summary>
    /// <param name="executionId">The execution id to cancel.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the cancellation was requested, otherwise <see langword="false"/>.</returns>
    Task<bool> TryCancelAsync(Guid executionId, CancellationToken cancellationToken = default);
}
