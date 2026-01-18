using System;

namespace Magpie.Models.Filters.Metadata;

/// <summary>
/// Filter criteria for <see cref="Models.Metadata.ReferenceMetadata"/>.
/// </summary>
public sealed record ReferenceMetadataCriteria
{
    /// <inheritdoc cref="Models.Metadata.ReferenceMetadata.Ordinal" />
    public AnyOfCriteria<FieldCriterion<int>> Ordinal { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.ReferenceMetadata.Kind" />
    public AnyOfCriteria<FieldCriterion<string>> Kind { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.ReferenceMetadata.Url" />
    public AnyOfCriteria<FieldCriterion<Uri>> Url { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.ReferenceMetadata.DisplayName" />
    public AnyOfCriteria<FieldCriterion<string>> DisplayName { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.ReferenceMetadata.Language" />
    public AnyOfCriteria<FieldCriterion<string>> Language { get; init; } = new();
}
