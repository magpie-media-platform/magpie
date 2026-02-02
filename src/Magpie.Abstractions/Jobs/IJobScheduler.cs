using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Abstractions.Jobs;

/// <summary>
/// Background service that watches schedule triggers and fires jobs.
/// </summary>
public interface IJobScheduler
{
    /// <summary>
    /// Start the scheduler.
    /// </summary>
    /// <param name="nodeId">The node identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task StartAsync(string nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop the scheduler gracefully.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task StopAsync(CancellationToken cancellationToken = default);
}
