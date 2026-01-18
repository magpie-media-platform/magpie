using System;

namespace Magpie.Models.Metadata;

/// <summary>
/// Patch for <see cref="ReferenceMetadata"/>.
/// </summary>
public sealed record ReferenceMetadataPatch
{
    /// <inheritdoc cref="ReferenceMetadata.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="ReferenceMetadata.Ordinal" />
    public FieldPatch<int>? Ordinal { get; init; }
    /// <inheritdoc cref="ReferenceMetadata.Kind" />
    public FieldPatch<string>? Kind { get; init; }
    /// <inheritdoc cref="ReferenceMetadata.Url" />
    public FieldPatch<Uri>? Url { get; init; }
    /// <inheritdoc cref="ReferenceMetadata.DisplayName" />
    public FieldPatch<string?>? DisplayName { get; init; }
    /// <inheritdoc cref="ReferenceMetadata.Language" />
    public FieldPatch<string?>? Language { get; init; }
}
