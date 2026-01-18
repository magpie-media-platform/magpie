using System;

using Magpie.Models.Filters.Metadata;

namespace Magpie.Models.Filters.Content;

/// <summary>
/// Filter criteria for a content hierarchy level.
/// </summary>
public sealed record ContentHierarchyLevelCriteria
{
    /// <inheritdoc cref="Models.Content.ContentHierarchyLevel.ContentHierarchyLevelDefinitionId" />
    public AnyOfCriteria<FieldCriterion<Guid>> ContentHierarchyLevelDefinitionId { get; init; } = new();
    /// <inheritdoc cref="Models.Content.ContentHierarchyLevel.Name" />
    public AnyOfCriteria<FieldCriterion<string>> Name { get; init; } = new();
    /// <inheritdoc cref="Models.Content.ContentHierarchyLevel.Slug" />
    public AnyOfCriteria<FieldCriterion<string>> Slug { get; init; } = new();
    /// <inheritdoc cref="Models.Content.ContentHierarchyLevel.RelativePath" />
    public AnyOfCriteria<FieldCriterion<string>> RelativePath { get; init; } = new();
    /// <inheritdoc cref="Models.Content.ContentHierarchyLevel.CreatedAt" />
    public AnyOfCriteria<FieldCriterion<DateTimeOffset>> CreatedAt { get; init; } = new();
    /// <inheritdoc cref="Models.Content.ContentHierarchyLevel.ModifiedAt" />
    public AnyOfCriteria<FieldCriterion<DateTimeOffset>> ModifiedAt { get; init; } = new();
    /// <inheritdoc cref="Models.Content.ContentHierarchyLevel.DateLastRefreshed" />
    public AnyOfCriteria<FieldCriterion<DateTimeOffset>> DateLastRefreshed { get; init; } = new();
    /// <inheritdoc cref="Models.Content.ContentHierarchyLevel.Metadata" />
    public AnyOfCriteria<MetadataContributionCriteria> Metadata { get; init; } = new();
    /// <inheritdoc cref="Models.Content.ContentHierarchyLevel.Tags" />
    public CollectionCriteria<FieldCriterion<string>> Tags { get; init; } = new();
}
