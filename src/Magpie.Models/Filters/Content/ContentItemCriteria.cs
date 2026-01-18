using System;
using System.Collections.Generic;

using Magpie.Models.Content;

namespace Magpie.Models.Filters.Content;

/// <summary>
/// Filter criteria for content items.
/// </summary>
public sealed record ContentItemCriteria
{
    /// <summary>
    /// The criteria for the target content hierarchy level.
    /// </summary>
    public required ContentHierarchyLevelCriteria TargetLevel { get; init; }
    /// <summary>
    /// The criteria for parent levels of the target content hierarchy level.
    /// </summary>
    public IReadOnlyList<ContentHierarchyLevelCriteria> ParentLevels { get; init; } = [];
    /// <inheritdoc cref="ContentItem.Monitoring" />
    public AnyOfCriteria<FieldCriterion<MonitoringIntent>> Monitoring { get; init; } = new();
    /// <inheritdoc cref="ContentItem.Fulfillment" />
    public AnyOfCriteria<FieldCriterion<FulfillmentStatus>> Fulfillment { get; init; } = new();
    /// <inheritdoc cref="ContentItem.QualityTierListId" />
    public AnyOfCriteria<FieldCriterion<Guid>> QualityTierListId { get; init; } = new();
    /// <inheritdoc cref="ContentItem.CreatedAt" />
    public AnyOfCriteria<FieldCriterion<DateTimeOffset>> CreatedAt { get; init; } = new();
    /// <inheritdoc cref="ContentItem.ModifiedAt" />
    public AnyOfCriteria<FieldCriterion<DateTimeOffset>> ModifiedAt { get; init; } = new();
    /// <inheritdoc cref="ContentItem.DateLastSearched" />
    public AnyOfCriteria<FieldCriterion<DateTimeOffset>> DateLastSearched { get; init; } = new();
    /// <inheritdoc cref="ContentItem.MediaFiles" />
    public CollectionCriteria<MediaFileCriteria> MediaFiles { get; init; } = new();
    /// <inheritdoc cref="ContentItem.Tags" />
    public CollectionCriteria<FieldCriterion<string>> Tags { get; init; } = new();
}
