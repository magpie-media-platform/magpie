namespace Magpie.Api.Request;

/// <summary>
/// Request to log out a user.
/// </summary>
/// <param name="RefreshToken">The refresh token to revoke.</param>
public sealed record LogoutRequest(string RefreshToken);
