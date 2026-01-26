using System;

using Magpie.Models.Filters.Metadata;

namespace Magpie.Models.Filters.Subject;

/// <summary>
/// Filter criteria for subjects.
/// </summary>
public sealed record SubjectCriteria
{
    /// <summary>The kind of subject.</summary>
    public AnyOfCriteria<FieldCriterion<string>> Kind { get; init; } = new();
    /// <summary>The date the subject metadata was last refreshed.</summary>
    public AnyOfCriteria<FieldCriterion<DateTimeOffset>> DateLastRefreshed { get; init; } = new();
    /// <summary>Tags applied to the subject.</summary>
    public CollectionCriteria<FieldCriterion<string>> Tags { get; init; } = new();
    /// <summary>When the subject was created.</summary>
    public AnyOfCriteria<FieldCriterion<DateTimeOffset>> CreatedAt { get; init; } = new();
    /// <summary>When the subject was last modified.</summary>
    public AnyOfCriteria<FieldCriterion<DateTimeOffset>> ModifiedAt { get; init; } = new();
    /// <summary>Criteria for the subject metadata.</summary>
    public AnyOfCriteria<MetadataContributionCriteria> Metadata { get; init; } = new();
}
