using System;

namespace Magpie.Models.Api;

/// <summary>
/// The response for a successful authentication request.
/// </summary>
public sealed record AuthTokenResponse
{
    /// <summary>
    /// The bearer access token.
    /// </summary>
    public required string AccessToken { get; init; }

    /// <summary>
    /// The time when the access token expires.
    /// </summary>
    public DateTime AccessTokenExpiresAt { get; init; }
}
