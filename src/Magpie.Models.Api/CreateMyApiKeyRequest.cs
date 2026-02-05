using System;

namespace Magpie.Models.Api;

/// <summary>
/// Request to create an API key for the current user.
/// </summary>
public sealed record CreateMyApiKeyRequest
{
    /// <summary>
    /// A user-friendly name for the key.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// When the key expires, if set.
    /// </summary>
    public DateTime? ExpiresAt { get; init; }

}
