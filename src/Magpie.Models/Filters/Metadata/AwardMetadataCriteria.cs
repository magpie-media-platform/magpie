namespace Magpie.Models.Filters.Metadata;

/// <summary>
/// Filter criteria for <see cref="Models.Metadata.AwardMetadata"/>.
/// </summary>
public sealed record AwardMetadataCriteria
{
    /// <inheritdoc cref="Models.Metadata.AwardMetadata.AwardBody" />
    public AnyOfCriteria<FieldCriterion<string>> AwardBody { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.AwardMetadata.Name" />
    public AnyOfCriteria<FieldCriterion<string>> Name { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.AwardMetadata.Category" />
    public AnyOfCriteria<FieldCriterion<string>> Category { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.AwardMetadata.Status" />
    public AnyOfCriteria<FieldCriterion<string>> Status { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.AwardMetadata.Year" />
    public AnyOfCriteria<FieldCriterion<int>> Year { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.AwardMetadata.Subject" />
    public AnyOfCriteria<FieldCriterion<string>> Subject { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.AwardMetadata.Language" />
    public AnyOfCriteria<FieldCriterion<string>> Language { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.AwardMetadata.Country" />
    public AnyOfCriteria<FieldCriterion<string>> Country { get; init; } = new();
}
