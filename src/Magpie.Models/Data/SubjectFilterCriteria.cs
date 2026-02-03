using System;

using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for <see cref="Subject"/>.
/// </summary>
public sealed record SubjectFilterCriteria
{
    /// <summary>
    /// Filter criteria for <see cref="Subject.Kind"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Kind { get; init; } = new();
    /// <summary>
    /// Filter criteria for <see cref="Subject.DateLastRefreshed"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<DateTime>> DateLastRefreshed { get; init; } = new();
    /// <summary>
    /// Filter criteria for the subject metadata.
    /// </summary>
    public CriteriaSet<MetadataCriteria> Metadata { get; init; } = new();
}
