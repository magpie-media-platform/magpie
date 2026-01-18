namespace Magpie.Models.Filters.Metadata;

/// <summary>
/// Filter criteria for <see cref="Models.Metadata.ContentRatingMetadata"/>.
/// </summary>
public sealed record ContentRatingMetadataCriteria
{
    /// <inheritdoc cref="Models.Metadata.ContentRatingMetadata.RatingsBody" />
    public AnyOfCriteria<FieldCriterion<string>> RatingsBody { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.ContentRatingMetadata.Value" />
    public AnyOfCriteria<FieldCriterion<string>> Value { get; init; } = new();
}
