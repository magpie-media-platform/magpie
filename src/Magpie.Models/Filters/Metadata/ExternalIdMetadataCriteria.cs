namespace Magpie.Models.Filters.Metadata;

/// <summary>
/// Filter criteria for <see cref="Models.Metadata.ExternalIdMetadata"/>.
/// </summary>
public sealed record ExternalIdMetadataCriteria
{
    /// <inheritdoc cref="Models.Metadata.ExternalIdMetadata.Ordinal" />
    public AnyOfCriteria<FieldCriterion<int>> Ordinal { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.ExternalIdMetadata.Value" />
    public AnyOfCriteria<FieldCriterion<string>> Value { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.ExternalIdMetadata.Attribution" />
    public AnyOfCriteria<FieldCriterion<string>> Attribution { get; init; } = new();
}
