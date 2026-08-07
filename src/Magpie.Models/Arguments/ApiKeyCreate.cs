using System;

namespace Magpie.Models.Arguments;

/// <summary>
/// The creation arguments for an API key bound to the calling user.
/// </summary>
public sealed record ApiKeyCreate
{
    /// <summary>
    /// The display name of the key.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The expiry time of the key, or <see langword="null"/> for no expiry.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }
}
