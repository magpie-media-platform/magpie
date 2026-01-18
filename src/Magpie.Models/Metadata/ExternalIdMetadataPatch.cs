using System;

namespace Magpie.Models.Metadata;

/// <summary>
/// Patch for <see cref="ExternalIdMetadata"/>.
/// </summary>
public sealed record ExternalIdMetadataPatch
{
    /// <inheritdoc cref="ExternalIdMetadata.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="ExternalIdMetadata.Ordinal" />
    public FieldPatch<int>? Ordinal { get; init; }
    /// <inheritdoc cref="ExternalIdMetadata.Value" />
    public FieldPatch<string>? Value { get; init; }
    /// <inheritdoc cref="ExternalIdMetadata.Attribution" />
    public FieldPatch<string?>? Attribution { get; init; }
}
