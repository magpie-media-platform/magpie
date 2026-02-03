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
    public CriteriaSet<PropertyCriterion<int>> Ordinal { get; init; } = new();
    /// <inheritdoc cref="Reference.Kind" />
    public CriteriaSet<PropertyCriterion<string>> Kind { get; init; } = new();
    /// <inheritdoc cref="Reference.Url" />
    public CriteriaSet<PropertyCriterion<Uri>> Url { get; init; } = new();
    /// <inheritdoc cref="Reference.DisplayName" />
    public CriteriaSet<PropertyCriterion<string>> DisplayName { get; init; } = new();
    /// <inheritdoc cref="Reference.Language" />
    public CriteriaSet<PropertyCriterion<string>> Language { get; init; } = new();
}
