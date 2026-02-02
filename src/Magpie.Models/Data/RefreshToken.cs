using System;

namespace Magpie.Models.Data;

/// <summary>
/// A refresh token for extending authentication sessions.
/// </summary>
public record RefreshToken
{
    /// <summary>
    /// The refresh token id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the user that this token belongs to.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The hashed refresh token value.
    /// </summary>
    public required string TokenHash { get; set; }

    /// <summary>
    /// When the token expires.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// When the token was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
