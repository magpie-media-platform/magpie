using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Jobs;

namespace Magpie.Abstractions.Jobs;

/// <summary>
/// An executable unit of work.
/// </summary>
/// <remarks>
/// Jobs represent discrete tasks that can be executed by the system, such as scanning
/// libraries, fetching metadata, searching for releases, or importing downloads. Jobs
/// may be triggered manually, scheduled to run periodically, or invoked in response
/// to events.
/// <para>
/// Job implementations are provided by extensions and registered with the job system
/// at startup.
/// </para>
/// </remarks>
public interface IJob
{
    /// <summary>
    /// The unique name of the job.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The display description of what the job does.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Executes the job.
    /// </summary>
    /// <param name="context">The execution context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task ExecuteAsync(JobContext context, CancellationToken cancellationToken);
}
