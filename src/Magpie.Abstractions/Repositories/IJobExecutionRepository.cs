using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Repository for managing job executions.
/// </summary>
public interface IJobExecutionRepository :
    ICrudRepository<Guid, JobExecution>,
    IQueryRepository<JobExecution, JobExecutionQuery>
{
    /// <summary>
    /// Atomically dequeue and claim a queued job execution.
    /// </summary>
    /// <param name="nodeId">The node claiming the execution.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The claimed job execution if one was available.</returns>
    Task<JobExecution?> TryDequeueAsync(string nodeId, CancellationToken cancellationToken = default);
}

