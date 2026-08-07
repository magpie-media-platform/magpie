using System;

namespace Magpie.Abstractions;

/// <summary>
/// The error for a shutdown request that failed.
/// </summary>
/// <param name="message">The text that describes the error</param>
/// <param name="actingUserId">The user that requested the shutdown, or <see langword="null"/></param>
/// <param name="requestedAtUtc">The UTC time of the shutdown request, or null when it is unknown</param>
public sealed class MagpieShutdownException(
    string message = "A shutdown request failed.",
    Guid? actingUserId = null,
    DateTime? requestedAtUtc = null) : MagpieException(message)
{
    /// <summary>
    /// The id of the user that requested the shutdown, or null when no user did.
    /// </summary>
    public Guid? ActingUserId { get; } = actingUserId;

    /// <summary>
    /// The UTC time of the shutdown request, or null when it is unknown.
    /// </summary>
    public DateTime? RequestedAtUtc { get; } = requestedAtUtc;
}
