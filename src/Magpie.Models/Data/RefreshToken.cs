using System;

namespace Magpie.Models.Data;

/// <summary>
/// A token that extends an authentication session.
/// </summary>
public record RefreshToken
{
    /// <summary>
    /// The unique id of the refresh token.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the user who holds the token.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The hash of the token value.
    /// </summary>
    public required string TokenHash { get; set; }

    /// <summary>
    /// The UTC time after which the token is no longer valid.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// The UTC time when the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
