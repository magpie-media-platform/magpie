namespace Magpie.Models.Api;

/// <summary>
/// Request to log out a user.
/// </summary>
public sealed record LogoutRequest
{
    /// <summary>
    /// The refresh token to revoke.
    /// </summary>
    public required string RefreshToken { get; init; }
}
