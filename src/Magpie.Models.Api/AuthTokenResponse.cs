using System;

namespace Magpie.Models.Api;

/// <summary>
/// Response containing authentication tokens.
/// </summary>
public sealed record AuthTokenResponse
{
    /// <summary>
    /// The JWT access token.
    /// </summary>
    public required string AccessToken { get; init; }

    /// <summary>
    /// When the access token expires.
    /// </summary>
    public DateTime AccessTokenExpiresAt { get; init; }

    /// <summary>
    /// The refresh token for obtaining new access tokens.
    /// </summary>
    public required string RefreshToken { get; init; }
}
