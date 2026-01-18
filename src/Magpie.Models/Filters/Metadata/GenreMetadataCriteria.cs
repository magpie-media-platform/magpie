namespace Magpie.Models.Filters.Metadata;

/// <summary>
/// Filter criteria for <see cref="Models.Metadata.GenreMetadata"/>.
/// </summary>
public sealed record GenreMetadataCriteria
{
    /// <inheritdoc cref="Models.Metadata.GenreMetadata.GenreName" />
    public AnyOfCriteria<FieldCriterion<string>> Genre { get; init; } = new();
}
