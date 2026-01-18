using System;
using System.Collections.Generic;

namespace Magpie.Models.Users;

/// <summary>
/// Patch for <see cref="AuthProvider"/>.
/// </summary>
public sealed record AuthProviderPatch
{
    /// <inheritdoc cref="AuthProvider.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="AuthProvider.Enabled" />
    public FieldPatch<bool>? Enabled { get; init; }
    /// <inheritdoc cref="AuthProvider.IsDefault" />
    public FieldPatch<bool>? IsDefault { get; init; }
    /// <inheritdoc cref="AuthProvider.Settings" />
    public FieldPatch<IReadOnlyDictionary<string, string?>>? Settings { get; init; }
}
