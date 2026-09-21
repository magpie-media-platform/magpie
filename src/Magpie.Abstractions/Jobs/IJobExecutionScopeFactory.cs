using System;

namespace Magpie.Abstractions.Jobs;

/// <summary>
/// The factory contract for job execution scopes.
/// </summary>
public interface IJobExecutionScopeFactory
{
    /// <summary>
    /// The id of the job execution that owns the current scope.
    /// </summary>
    Guid? CurrentExecutionId { get; }

    /// <summary>
    /// The correlation of the job execution that owns the current scope.
    /// </summary>
    Guid? CurrentCorrelationId { get; }

    /// <summary>
    /// Creates a scope for one job execution, which keeps the correlation of the current scope.
    /// </summary>
    /// <param name="executionId">The id of the job execution that the scope serves</param>
    IJobExecutionScope CreateScope(Guid executionId);
}
