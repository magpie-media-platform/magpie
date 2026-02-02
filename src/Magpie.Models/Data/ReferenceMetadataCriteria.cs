using System;

using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for <see cref="Reference"/>.
/// </summary>
public sealed record ReferenceMetadataCriteria
{
    /// <summary>
    /// The criteria for <see cref="Reference.Ordinal"/>.
    /// </summary>
    public AllCriteria<PropertyCriterion<int>> Ordinal { get; init; } = new();
    /// <inheritdoc cref="Reference.Kind" />
    public AllCriteria<PropertyCriterion<string>> Kind { get; init; } = new();
    /// <inheritdoc cref="Reference.Url" />
    public AllCriteria<PropertyCriterion<Uri>> Url { get; init; } = new();
    /// <inheritdoc cref="Reference.DisplayName" />
    public AllCriteria<PropertyCriterion<string>> DisplayName { get; init; } = new();
    /// <inheritdoc cref="Reference.Language" />
    public AllCriteria<PropertyCriterion<string>> Language { get; init; } = new();
}
