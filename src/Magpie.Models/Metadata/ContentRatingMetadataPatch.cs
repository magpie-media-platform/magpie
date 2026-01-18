using System;

namespace Magpie.Models.Metadata;

/// <summary>
/// Patch for <see cref="ContentRatingMetadata"/>.
/// </summary>
public sealed record ContentRatingMetadataPatch
{
    /// <inheritdoc cref="ContentRatingMetadata.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="ContentRatingMetadata.Ordinal" />
    public FieldPatch<int>? Ordinal { get; init; }
    /// <inheritdoc cref="ContentRatingMetadata.RatingsBody" />
    public FieldPatch<string>? RatingsBody { get; init; }
    /// <inheritdoc cref="ContentRatingMetadata.Value" />
    public FieldPatch<string>? Value { get; init; }
}
