using System;

using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// The filter criteria for a <see cref="Reference"/>.
/// </summary>
public sealed record ReferenceMetadataCriteria
{
    /// <summary>
    /// The criteria on the position in display order.
    /// </summary>
    public CriteriaSet<PropertyCriterion<int>> Ordinal { get; set; } = new();
    /// <inheritdoc cref="Reference.Kind" />
    public CriteriaSet<PropertyCriterion<string>> Kind { get; set; } = new();
    /// <inheritdoc cref="Reference.Url" />
    public CriteriaSet<PropertyCriterion<Uri>> Url { get; set; } = new();
    /// <inheritdoc cref="Reference.DisplayName" />
    public CriteriaSet<PropertyCriterion<string>> DisplayName { get; set; } = new();
    /// <inheritdoc cref="Reference.Language" />
    public CriteriaSet<PropertyCriterion<string>> Language { get; set; } = new();
}
