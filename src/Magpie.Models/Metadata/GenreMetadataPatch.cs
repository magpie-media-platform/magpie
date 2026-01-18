using System;

namespace Magpie.Models.Metadata;

/// <summary>
/// Patch for <see cref="GenreMetadata"/>.
/// </summary>
public sealed record GenreMetadataPatch
{
    /// <inheritdoc cref="GenreMetadata.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="GenreMetadata.Ordinal" />
    public FieldPatch<int>? Ordinal { get; init; }
    /// <inheritdoc cref="GenreMetadata.GenreName" />
    public FieldPatch<string>? Genre { get; init; }
}
