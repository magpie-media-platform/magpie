using System;

namespace Magpie.Models.Arguments;

/// <summary>
/// The query filters for API keys.
/// </summary>
public sealed record ApiKeyQuery
{
    /// <summary>
    /// The key hash to match, or <c>null</c> for all hashes.
    /// </summary>
    public string? KeyHash { get; init; }

    /// <summary>
    /// The id of the owning user to match.
    /// </summary>
    public Guid? UserId { get; init; }

    /// <summary>
    /// The enabled state to match.
    /// </summary>
    public bool? Enabled { get; init; }

    /// <summary>
    /// The expiry state to match at the current time, where <see langword="false"/> matches keys with no expiry.
    /// </summary>
    public bool? Expired { get; init; }
}
