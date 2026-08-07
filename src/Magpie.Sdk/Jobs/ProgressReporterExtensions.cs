using System;

namespace Magpie.Sdk.Jobs;

/// <summary>
/// Extension methods for proportional job progress.
/// </summary>
public static class ProgressReporterExtensions
{
    /// <summary>
    /// Reports progress as a bounded fraction of the total item count.
    /// </summary>
    /// <param name="reporter">The progress channel of the current job execution</param>
    /// <param name="completed">The number of items that are complete</param>
    /// <param name="total">The number of items in the batch</param>
    /// <param name="message">The status message for the current step</param>
    public static void ReportFraction(this IProgressReporter reporter, int completed, int total, string? message = null)
    {
        double? percentage = total <= 0
            ? null
            : Math.Clamp(completed / (double)total, 0, 1);

        reporter.Report(percentage, message);
    }
}
