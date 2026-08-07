using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;

namespace Magpie.Sdk.Jobs;

/// <summary>
/// An executable unit of work.
/// </summary>
public interface IJob
{
    /// <summary>
    /// Runs the job.
    /// </summary>
    /// <param name="context">The context of the current job execution</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default);
}
