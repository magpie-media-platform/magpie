using System;

using Magpie.Models.Enums;

namespace Magpie.Models.Data;

/// <summary>
/// The result of a request to retry a download.
/// </summary>
public record DownloadRetryResult
{
    /// <summary>
    /// The outcome of the retry request.
    /// </summary>
    public required DownloadRetryOutcome Outcome { get; init; }

    /// <summary>
    /// The id of the content item of the retried download, or <c>null</c> if Magpie retried nothing.
    /// </summary>
    public Guid? ContentItemId { get; init; }
}
