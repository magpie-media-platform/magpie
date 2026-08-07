using System;
using System.Collections.Generic;

using Magpie.Models.Enums;
using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for content hierarchy items.
/// </summary>
public sealed record ContentHierarchyItemCriteria
{
    /// <summary>
    /// Criteria for the id of the parent item.
    /// </summary>
    public CriteriaSet<PropertyCriterion<Guid>> ParentId { get; init; } = new();

    /// <summary>
    /// Criteria for the id of the root folder.
    /// </summary>
    public CriteriaSet<PropertyCriterion<Guid>> RootFolderId { get; init; } = new();

    /// <summary>
    /// The id of the content item to match.
    /// </summary>
    public Guid? ContentItemId { get; init; }

    /// <summary>
    /// Criteria for the slug of the content type.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> ContentType { get; init; } = new();

    /// <summary>
    /// Criteria for the level in the content hierarchy.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Level { get; init; } = new();

    /// <summary>
    /// Criteria for the slug of the item.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Slug { get; init; } = new();

    /// <summary>
    /// Criteria for the display name of the item.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Name { get; init; } = new();

    /// <summary>
    /// Criteria for the monitoring value of the item.
    /// </summary>
    public CriteriaSet<PropertyCriterion<bool>> Monitoring { get; init; } = new();

    /// <summary>
    /// The effective monitoring value to match.
    /// </summary>
    public bool? EffectiveMonitoring { get; init; }

    /// <summary>
    /// The effective root folder id to match.
    /// </summary>
    public Guid? EffectiveRootFolderId { get; init; }

    /// <summary>
    /// Criteria for the fulfillment status.
    /// </summary>
    public CriteriaSet<PropertyCriterion<FulfillmentStatus>> Fulfillment { get; init; } = new();

    /// <summary>
    /// Criteria for the status of the active acquisition.
    /// </summary>
    public CriteriaSet<PropertyCriterion<DownloadStatus>> ActiveAcquisitionStatus { get; init; } = new();

    /// <summary>
    /// Criteria for the <see cref="QualityStatus"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<QualityStatus>> QualityStatus { get; init; } = new();

    /// <summary>
    /// Criteria for the file system path.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Path { get; init; } = new();

    /// <summary>
    /// Criteria for the tags of the item.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Tags { get; init; } = new();

    /// <summary>
    /// Criteria for the metadata of the item.
    /// </summary>
    public CriteriaSet<MetadataCriteria> Metadata { get; init; } = new();

    /// <summary>
    /// Whether the result contains only root-level items.
    /// </summary>
    public bool? RootLevelOnly { get; init; }

    /// <summary>
    /// Whether the result contains only leaf items.
    /// </summary>
    public bool? LeafOnly { get; init; }

    /// <summary>
    /// Whether the result contains only items that await a first metadata refresh.
    /// </summary>
    public bool? PendingMetadataRefresh { get; init; }

    /// <summary>
    /// Whether the result contains only items with metadata external ids.
    /// </summary>
    public bool? HasMetadataExternalIds { get; init; }

    /// <summary>
    /// Whether the result contains only items with a searchable title.
    /// </summary>
    public bool? HasSearchableTitle { get; init; }

    /// <summary>
    /// Whether the result contains only items with a monitored parent.
    /// </summary>
    public bool? HasMonitoredParent { get; init; }

    /// <summary>
    /// The ids to restrict the result to.
    /// </summary>
    public IReadOnlyCollection<Guid>? Ids { get; init; }

    /// <summary>
    /// The ids to exclude from the result.
    /// </summary>
    public IReadOnlyCollection<Guid>? ExcludeIds { get; init; }

    /// <summary>
    /// The id of an ancestor that matches its descendants at any depth.
    /// </summary>
    public Guid? AncestorId { get; init; }
}
