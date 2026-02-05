namespace Magpie.Models.Api;

/// <summary>
/// Request to log in a user.
/// </summary>
public sealed record LoginRequest
{
    /// <summary>
    /// The username of the user.
    /// </summary>
    public required string Username { get; init; }

    /// <summary>
    /// The supplied password for the user.
    /// </summary>
    public required string Password { get; init; }
}
