using System;

namespace Magpie.Models.Users;

/// <summary>
/// Patch for <see cref="User"/>.
/// </summary>
public sealed record UserPatch
{
    /// <inheritdoc cref="User.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="User.Enabled" />
    public FieldPatch<bool>? Enabled { get; init; }
    /// <inheritdoc cref="User.Role" />
    public FieldPatch<Role>? Role { get; init; }
}
