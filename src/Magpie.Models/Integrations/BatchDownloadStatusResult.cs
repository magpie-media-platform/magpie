using System;
using System.Collections.Generic;

namespace Magpie.Models.Integrations;

/// <summary>
/// The result of a batched download-status lookup.
/// </summary>
public sealed record BatchDownloadStatusResult
{
    /// <summary>
    /// Creates a batched download-status result.
    /// </summary>
    /// <param name="statuses">The status results, keyed by requested id</param>
    /// <param name="failures">The failures, keyed by requested id</param>
    public BatchDownloadStatusResult(
        IReadOnlyDictionary<string, DownloadInfo> statuses,
        IReadOnlyDictionary<string, Exception> failures)
    {
        if (statuses is null)
        {
            throw new ArgumentNullException(nameof(statuses));
        }

        if (failures is null)
        {
            throw new ArgumentNullException(nameof(failures));
        }

        Statuses = statuses;
        Failures = failures;
    }

    /// <summary>
    /// The status results, keyed by requested id.
    /// </summary>
    public IReadOnlyDictionary<string, DownloadInfo> Statuses { get; }

    /// <summary>
    /// The failures, keyed by requested id.
    /// </summary>
    public IReadOnlyDictionary<string, Exception> Failures { get; }
}
