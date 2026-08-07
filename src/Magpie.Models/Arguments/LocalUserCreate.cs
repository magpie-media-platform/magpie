using Magpie.Models.Enums;

namespace Magpie.Models.Arguments;

/// <summary>
/// The creation arguments for a local user with a plaintext password.
/// </summary>
public sealed record LocalUserCreate
{
    /// <summary>
    /// The username of the user.
    /// </summary>
    public required string Username { get; set; }

    /// <summary>
    /// The plaintext password to hash and store.
    /// </summary>
    public required string Password { get; set; }

    /// <summary>
    /// The role to grant the user.
    /// </summary>
    public required Role Role { get; set; }

    /// <summary>
    /// Whether the user can sign in.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
