namespace Magpie.Models.Api;

/// <summary>
/// The request to authenticate a user.
/// </summary>
public sealed record LoginRequest
{
    /// <summary>
    /// The username of the account to authenticate.
    /// </summary>
    public required string Username { get; init; }

    /// <summary>
    /// The password for the account.
    /// </summary>
    public required string Password { get; init; }
}
