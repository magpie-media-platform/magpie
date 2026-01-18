using System;

namespace Magpie.Models.Extensions;

/// <summary>
/// Patch for <see cref="Extension"/>.
/// </summary>
public sealed record ExtensionPatch
{
    /// <inheritdoc cref="Extension.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="Extension.Version" />
    public FieldPatch<string>? Version { get; init; }
    /// <inheritdoc cref="Extension.Enabled" />
    public FieldPatch<bool>? Enabled { get; init; }
    /// <inheritdoc cref="Extension.DisabledReason" />
    public FieldPatch<string?>? DisabledReason { get; init; }
    /// <inheritdoc cref="Extension.LoadOrder" />
    public FieldPatch<int>? LoadOrder { get; init; }
    /// <inheritdoc cref="Extension.AutoUpgrade" />
    public FieldPatch<bool>? AutoUpgrade { get; init; }
}
