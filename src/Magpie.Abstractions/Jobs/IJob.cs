using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Enums;

namespace Magpie.Abstractions.Jobs;

/// <summary>
/// An executable unit of work.
/// </summary>
public interface IJob
{
    /// <summary>
    /// The unique type identifier of the job.
    /// </summary>
    string JobType { get; }

    /// <summary>
    /// The display name of the job.
    /// </summary>
    string? DisplayName { get; }

    /// <summary>
    /// The display description of what the job does.
    /// </summary>
    string? Description { get; }

    /// <summary>
    /// The scope at which the job operates.
    /// </summary>
    JobScope Scope { get; }

    /// <summary>
    /// The parameters this job accepts.
    /// </summary>
    IEnumerable<JobParameter> Parameters { get; }

    /// <summary>
    /// Whether the job can be run on demand (manually triggered).
    /// </summary>
    bool OnDemand { get; }

    /// <summary>
    /// The maximum time the job is allowed to run before being forcibly cancelled.
    /// </summary>
    /// <remarks>
    /// If null, the job has no timeout and can run indefinitely.
    /// </remarks>
    TimeSpan? Timeout { get; }

    /// <summary>
    /// Execute the job.
    /// </summary>
    /// <param name="context">The execution context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if the job was successfully started, otherwise <see langword="false"/>.
    /// </returns>
    Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default);
}
