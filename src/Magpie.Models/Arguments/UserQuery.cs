using Magpie.Models.Data;
using Magpie.Models.Enums;

namespace Magpie.Models.Arguments;

/// <summary>
/// Query arguments for <see cref="User"/>.
/// </summary>
public record UserQuery
{
    /// <summary>
    /// The criteria for <see cref="User.Enabled"/>.
    /// </summary>
    public bool? Enabled { get; init; }

    /// <summary>
    /// The criteria for <see cref="User.Role"/>.
    /// </summary>
    public Role? Role { get; init; }

    /// <summary>
    /// The criteria for <see cref="User.ProviderType"/>.
    /// </summary>
    public string? ProviderType { get; init; }
}
