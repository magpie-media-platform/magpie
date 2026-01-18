namespace Magpie.Models.Filters.Metadata;

/// <summary>
/// Filter criteria for <see cref="Models.Metadata.ImageMetadata"/>.
/// </summary>
public sealed record ImageMetadataCriteria
{
    /// <inheritdoc cref="Models.Metadata.ImageMetadata.ImageType" />
    public AnyOfCriteria<FieldCriterion<string>> ImageType { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.ImageMetadata.MimeType" />
    public AnyOfCriteria<FieldCriterion<string>> MimeType { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.ImageMetadata.Attribution" />
    public AnyOfCriteria<FieldCriterion<string>> Attribution { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.ImageMetadata.Language" />
    public AnyOfCriteria<FieldCriterion<string>> Language { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.ImageMetadata.Width" />
    public AnyOfCriteria<FieldCriterion<int>> Width { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.ImageMetadata.Height" />
    public AnyOfCriteria<FieldCriterion<int>> Height { get; init; } = new();
}
