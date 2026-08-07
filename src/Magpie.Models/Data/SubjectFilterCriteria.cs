using System;

using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// The filter criteria for a <see cref="Subject"/>.
/// </summary>
public sealed record SubjectFilterCriteria
{
    /// <summary>
    /// The criteria on the kind of subject.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Kind { get; init; } = new();

    /// <summary>
    /// The criteria on the UTC time of the last refresh.
    /// </summary>
    public CriteriaSet<PropertyCriterion<DateTime>> DateLastRefreshed { get; init; } = new();

    /// <summary>
    /// The criteria on the metadata of the subject.
    /// </summary>
    public CriteriaSet<MetadataCriteria> Metadata { get; init; } = new();
}
