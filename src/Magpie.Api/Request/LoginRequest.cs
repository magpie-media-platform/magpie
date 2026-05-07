namespace Magpie.Api.Request;

/// <summary>
/// Request to log in a user.
/// </summary>
/// <param name="Username">The username of the user.</param>
/// <param name="Password">The supplied password for the user.</param>
public sealed record LoginRequest(string Username, string Password);
