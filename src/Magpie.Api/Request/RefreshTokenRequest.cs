namespace Magpie.Api.Request;

/// <summary>
/// Request to refresh an access token.
/// </summary>
/// <param name="RefreshToken">The refresh token to use.</param>
public sealed record RefreshTokenRequest(string RefreshToken);
