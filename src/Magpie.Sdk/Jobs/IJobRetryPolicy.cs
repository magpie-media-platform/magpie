using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;

namespace Magpie.Sdk.Jobs;

/// <summary>
/// The retry policy for failed job runs.
/// </summary>
public interface IJobRetryPolicy
{
    /// <summary>
    /// Creates the retry execution for a failed run, or null at the retry ceiling.
    /// </summary>
    /// <param name="failed">The failed job execution</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<JobExecution?> BuildRetryAsync(JobExecution failed, CancellationToken cancellationToken = default);
}
