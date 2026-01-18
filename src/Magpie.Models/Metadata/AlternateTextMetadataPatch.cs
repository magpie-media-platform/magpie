using System;

namespace Magpie.Models.Metadata;

/// <summary>
/// Patch for <see cref="AlternateTextMetadata"/>.
/// </summary>
public sealed record AlternateTextMetadataPatch
{
    /// <inheritdoc cref="AlternateTextMetadata.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="AlternateTextMetadata.Ordinal" />
    public FieldPatch<int>? Ordinal { get; init; }
    /// <inheritdoc cref="AlternateTextMetadata.Type" />
    public FieldPatch<string>? Type { get; init; }
    /// <inheritdoc cref="AlternateTextMetadata.Language" />
    public FieldPatch<string?>? Language { get; init; }
    /// <inheritdoc cref="AlternateTextMetadata.Text" />
    public FieldPatch<string>? Text { get; init; }
}
