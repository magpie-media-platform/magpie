using System;
using System.Diagnostics.CodeAnalysis;

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
    void SetParentContext(Guid executionId);

    /// <summary>
    /// Gets the handler that is registered for a job specification.
    /// </summary>
    /// <param name="spec">The specification of the job to run</param>
    /// <param name="handler">The registered handler, set only when the method returns true</param>
    bool TryGetHandler(JobSpec spec, [MaybeNullWhen(false)] out IJob handler);
}
