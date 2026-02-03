using Magpie.Models.Data;
using Magpie.Models.Enums;

namespace Magpie.Models.Arguments;

/// <summary>
/// Arguments for creating a local <see cref="User"/>.
/// </summary>
public record UserCreate
{
    /// <summary>
    /// The <see cref="User.Username"/> for the new user.
    /// </summary>
    public required string Username { get; init; }

    /// <summary>
    /// The <see cref="User.PasswordHash"/> for the new user.
    /// </summary>
    public required string PasswordHash { get; init; }

    /// <summary>
    /// The <see cref="User.Role"/> for the new user.
    /// </summary>
    public required Role Role { get; init; }
}
