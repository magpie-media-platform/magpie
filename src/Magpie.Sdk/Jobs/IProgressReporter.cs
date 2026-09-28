using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Sdk.Jobs;

/// <summary>
/// The progress channel of a running job.
/// </summary>
public interface IProgressReporter
{
    /// <summary>
    /// Reports progress for the current job execution.
    /// </summary>
    /// <param name="percentage">The progress fraction from 0.0 to 1.0, or null for indeterminate progress</param>
    /// <param name="message">The status message for the current step</param>
    void Report(double? percentage, string? message);

    /// <summary>
    /// Waits for pending progress reports to finish.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the wait</param>
    Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
