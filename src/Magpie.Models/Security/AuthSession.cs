using System;

using Magpie.Models.Data;
namespace Magpie.Models.Security;

/// <summary>
/// An authenticated session with its user, its tokens, and the expiry times of those tokens.
/// </summary>
public sealed record AuthSession
{
    /// <summary>
    /// The authenticated user.
    /// </summary>
    public required User User { get; set; }

    /// <summary>
    /// The access token.
    /// </summary>
    public required string AccessToken { get; set; }

    /// <summary>
    /// The UTC expiry time of the access token.
    /// </summary>
    public required DateTime AccessTokenExpiresAt { get; set; }

    /// <summary>
    /// The refresh token.
    /// </summary>
    public required string RefreshToken { get; set; }

    /// <summary>
    /// The UTC expiry time of the refresh token.
    /// </summary>
    public required DateTime RefreshTokenExpiresAt { get; set; }
}
