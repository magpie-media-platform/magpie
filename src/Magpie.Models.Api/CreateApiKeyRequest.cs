using System;

namespace Magpie.Models.Api;

/// <summary>
/// Request to create an API key.
/// </summary>
public sealed record CreateApiKeyRequest
{
    /// <summary>
    /// The user id to associate the key with.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// A user-friendly name for the key.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// When the key expires, if set.
    /// </summary>
    public DateTime? ExpiresAt { get; init; }
}
