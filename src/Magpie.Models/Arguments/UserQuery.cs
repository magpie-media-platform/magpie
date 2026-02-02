using Magpie.Models.Data;
using Magpie.Models.Enums;

namespace Magpie.Models.Arguments;

/// <summary>
/// Query arguments for <see cref="User"/>.
/// </summary>
/// <param name="Enabled">The criteria for <see cref="User.Enabled"/>.</param>
/// <param name="Role">The criteria for <see cref="User.Role"/>.</param>
/// <param name="ProviderType">The criteria for <see cref="User.ProviderType"/>.</param>
public record UserQuery(
    bool? Enabled = null,
    Role? Role = null,
    string? ProviderType = null);

/// <summary>
/// Arguments for creating a local <see cref="User"/>.
/// </summary>
/// <param name="Username">The username.</param>
/// <param name="Email">The email address.</param>
/// <param name="Password">The plaintext password.</param>
public record UserCreate(
    string Username,
    string Email,
    string Password);
