using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Triggers;

namespace Magpie.Abstractions.Jobs;

/// <summary>
/// Provides job scheduling and execution capabilities.
/// </summary>
public interface ISchedulingProvider
{
    /// <summary>
    /// Schedules a job to run periodically.
    /// </summary>
    /// <param name="trigger">The schedule trigger configuration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task ScheduleAsync(ScheduleTrigger trigger, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a scheduled job.
    /// </summary>
    /// <param name="jobName">The name of the job to unschedule.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task UnscheduleAsync(string jobName, CancellationToken cancellationToken);

    /// <summary>
    /// Executes a job immediately.
    /// </summary>
    /// <param name="jobName">The name of the job to execute.</param>
    /// <param name="parameters">Parameters to pass to the job.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task ExecuteNowAsync(
        string jobName,
        IReadOnlyDictionary<string, string>? parameters,
        CancellationToken cancellationToken
    );
}
