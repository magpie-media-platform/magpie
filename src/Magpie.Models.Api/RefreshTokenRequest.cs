namespace Magpie.Models.Api;

/// <summary>
/// Request to refresh an access token.
/// </summary>
public sealed record RefreshTokenRequest
{
    /// <summary>
    /// The refresh token.
    /// </summary>
    public required string RefreshToken { get; init; }
}
