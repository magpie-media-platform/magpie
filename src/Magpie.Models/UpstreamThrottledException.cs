using System;

namespace Magpie.Models;

/// <summary>
/// An upstream cooldown that prevents completion of the current work.
/// </summary>
/// <remarks>
/// Creates an upstream cooldown result.
/// </remarks>
/// <param name="retryAt">The earliest time to resume the work</param>
/// <param name="requestSent">True when the upstream received the request</param>
public sealed class UpstreamThrottledException(
    DateTimeOffset retryAt,
    bool requestSent = false) : Exception(
        "The upstream is throttled. The work is incomplete and will resume after the cooldown.")
{
    /// <summary>
    /// The earliest time to resume the work.
    /// </summary>
    public DateTimeOffset RetryAt { get; } = retryAt;

    /// <summary>
    /// True when the upstream received the request.
    /// </summary>
    public bool RequestSent { get; } = requestSent;
}
