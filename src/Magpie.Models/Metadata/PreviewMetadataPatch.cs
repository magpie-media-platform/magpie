using System;

namespace Magpie.Models.Metadata;

/// <summary>
/// Patch for <see cref="PreviewMetadata"/>.
/// </summary>
public sealed record PreviewMetadataPatch
{
    /// <inheritdoc cref="PreviewMetadata.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="PreviewMetadata.Ordinal" />
    public FieldPatch<int>? Ordinal { get; init; }
    /// <inheritdoc cref="PreviewMetadata.PreviewType" />
    public FieldPatch<string>? PreviewType { get; init; }
    /// <inheritdoc cref="PreviewMetadata.MimeType" />
    public FieldPatch<string?>? MimeType { get; init; }
    /// <inheritdoc cref="PreviewMetadata.Url" />
    public FieldPatch<Uri>? Url { get; init; }
    /// <inheritdoc cref="PreviewMetadata.Attribution" />
    public FieldPatch<string?>? Attribution { get; init; }
    /// <inheritdoc cref="PreviewMetadata.Title" />
    public FieldPatch<string?>? Title { get; init; }
    /// <inheritdoc cref="PreviewMetadata.ReleaseDate" />
    public FieldPatch<DateTime?>? ReleaseDate { get; init; }
    /// <inheritdoc cref="PreviewMetadata.Language" />
    public FieldPatch<string?>? Language { get; init; }
}
