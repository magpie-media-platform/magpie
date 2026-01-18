using System;

namespace Magpie.Models.Filters.Metadata;

/// <summary>
/// Filter criteria for <see cref="Models.Metadata.PreviewMetadata"/>.
/// </summary>
public sealed record PreviewMetadataCriteria
{
    /// <inheritdoc cref="Models.Metadata.PreviewMetadata.PreviewType" />
    public AnyOfCriteria<FieldCriterion<string>> PreviewType { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.PreviewMetadata.MimeType" />
    public AnyOfCriteria<FieldCriterion<string>> MimeType { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.PreviewMetadata.Attribution" />
    public AnyOfCriteria<FieldCriterion<string>> Attribution { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.PreviewMetadata.Title" />
    public AnyOfCriteria<FieldCriterion<string>> Title { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.PreviewMetadata.ReleaseDate" />
    public AnyOfCriteria<FieldCriterion<DateTime>> ReleaseDate { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.PreviewMetadata.Language" />
    public AnyOfCriteria<FieldCriterion<string>> Language { get; init; } = new();
}
