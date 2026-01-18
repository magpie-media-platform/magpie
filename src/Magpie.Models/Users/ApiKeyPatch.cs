using System;

namespace Magpie.Models.Users;

/// <summary>
/// Patch for <see cref="ApiKey"/>.
/// </summary>
public sealed record ApiKeyPatch
{
    /// <inheritdoc cref="ApiKey.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="ApiKey.Name" />
    public FieldPatch<string>? Name { get; init; }
    /// <inheritdoc cref="ApiKey.Enabled" />
    public FieldPatch<bool>? Enabled { get; init; }
    /// <inheritdoc cref="ApiKey.ExpiresAt" />
    public FieldPatch<DateTimeOffset?>? ExpiresAt { get; init; }
}
