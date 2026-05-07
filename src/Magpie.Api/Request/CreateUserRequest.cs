using Magpie.Models.Enums;

namespace Magpie.Api.Request;

/// <summary>
/// Request to create a local user account.
/// </summary>
/// <param name="Username">The username for the new user.</param>
/// <param name="Password">The plaintext password for the new user.</param>
/// <param name="Role">The role for the new user.</param>
public sealed record CreateUserRequest(string Username, string Password, Role Role);
