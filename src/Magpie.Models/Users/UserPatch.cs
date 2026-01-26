using System;

namespace Magpie.Models.Users;

/// <summary>
/// Patch for <see cref="User"/>.
/// </summary>
public sealed record UserPatch
{
    /// <inheritdoc cref="User.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="User.AuthProviderId" />
    public FieldPatch<Guid?>? AuthProviderId { get; init; }
    /// <inheritdoc cref="User.ExternalId" />
    public FieldPatch<string?>? ExternalId { get; init; }
    /// <inheritdoc cref="User.Enabled" />
    public FieldPatch<bool>? Enabled { get; init; }
    /// <inheritdoc cref="User.Role" />
    public FieldPatch<Role>? Role { get; init; }
    /// <inheritdoc cref="User.ProviderData" />
    public DictionaryPatch<string, string, string>? ProviderData { get; init; }
}
