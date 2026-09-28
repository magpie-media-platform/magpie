using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Sdk.Jobs;

namespace Magpie.Abstractions.Jobs;

/// <summary>
/// The dependency scope that supplies the job handlers for one job execution.
/// </summary>
public interface IJobExecutionScope : IAsyncDisposable
{
    /// <summary>
    /// Sets the parent execution for the jobs that this scope enqueues.
    /// </summary>
    /// <param name="executionId">The id of the parent job execution</param>
    /// <param name="correlationId">The correlation of the parent job execution</param>
    void SetParentContext(Guid executionId, Guid? correlationId);

    /// <summary>
    /// Gets the handler that is registered for a job specification, if one exists.
    /// </summary>
    /// <param name="spec">The specification of the job to run</param>
    /// <param name="handler">The registered handler, set only when the method returns true</param>
    bool TryGetHandler(JobSpec spec, [MaybeNullWhen(false)] out IJob handler);

    /// <summary>
    /// Waits for the scope's pending progress reports to finish.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the wait</param>
    Task FlushProgressAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
