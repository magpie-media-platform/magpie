namespace Magpie.Abstractions.Jobs;

/// <summary>
/// Allows jobs to report progress during execution.
/// </summary>
public interface IProgressReporter
{
    /// <summary>
    /// Reports progress for the current job execution.
    /// </summary>
    /// <param name="percentage">The progress percentage from 0.0 to 1.0, or null if indeterminate.</param>
    /// <param name="message">A status message describing the current operation.</param>
    void Report(double? percentage, string? message);
}
