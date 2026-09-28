using System;

namespace Magpie.Models;

/// <summary>
/// A release payload that was rejected before anything was sent to the download client.
/// </summary>
/// <param name="message">The reason, without URLs, credentials, cookies or passkeys</param>
/// <param name="transient">Whether the rejection can go away, for example a rate limit or a server error</param>
public sealed class ReleasePayloadRejectedException(string message, bool transient) : Exception(message)
{
    /// <summary>
    /// Creates a permanent rejection.
    /// </summary>
    /// <param name="message">The reason, without URLs, credentials, cookies or passkeys</param>
    public ReleasePayloadRejectedException(string message)
        : this(message, false)
    {
    }

    /// <summary>
    /// Whether the rejection can go away, so a later grab of the release can succeed.
    /// </summary>
    public bool Transient { get; } = transient;
}
