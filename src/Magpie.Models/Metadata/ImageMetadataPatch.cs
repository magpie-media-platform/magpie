using System;

namespace Magpie.Models.Metadata;

/// <summary>
/// Patch for <see cref="ImageMetadata"/>.
/// </summary>
public sealed record ImageMetadataPatch
{
    /// <inheritdoc cref="ImageMetadata.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="ImageMetadata.Ordinal" />
    public FieldPatch<int>? Ordinal { get; init; }
    /// <inheritdoc cref="ImageMetadata.ImageType" />
    public FieldPatch<string>? ImageType { get; init; }
    /// <inheritdoc cref="ImageMetadata.MimeType" />
    public FieldPatch<string?>? MimeType { get; init; }
    /// <inheritdoc cref="ImageMetadata.Url" />
    public FieldPatch<Uri>? Url { get; init; }
    /// <inheritdoc cref="ImageMetadata.Attribution" />
    public FieldPatch<string?>? Attribution { get; init; }
    /// <inheritdoc cref="ImageMetadata.Language" />
    public FieldPatch<string?>? Language { get; init; }
    /// <inheritdoc cref="ImageMetadata.Width" />
    public FieldPatch<int?>? Width { get; init; }
    /// <inheritdoc cref="ImageMetadata.Height" />
    public FieldPatch<int?>? Height { get; init; }
}
