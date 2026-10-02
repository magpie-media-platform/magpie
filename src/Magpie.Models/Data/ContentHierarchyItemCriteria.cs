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
    /// Criteria for the stored primary-media size, or null before initialization.
    /// </summary>
    public CriteriaSet<PropertyCriterion<long>> PrimaryMediaSize { get; set; } = new();

    /// <summary>
    /// Criteria for the stored primary-media file count, or null before initialization.
    /// </summary>
    public CriteriaSet<PropertyCriterion<int>> PrimaryMediaFileCount { get; set; } = new();

    /// <summary>
    /// Criteria for the id of the parent item.
    /// </summary>
    public CriteriaSet<PropertyCriterion<Guid>> ParentId { get; set; } = new();

    /// <summary>
    /// Criteria for the id of the root folder.
    /// </summary>
    public CriteriaSet<PropertyCriterion<Guid>> RootFolderId { get; set; } = new();

    /// <summary>
    /// The id of the content item to match.
    /// </summary>
    public Guid? ContentItemId { get; set; }

    /// <summary>
    /// Criteria for the slug of the content type.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> ContentType { get; set; } = new();

    /// <summary>
    /// Criteria for the level in the content hierarchy.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Level { get; set; } = new();

    /// <summary>
    /// Criteria for the slug of the item.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Slug { get; set; } = new();

    /// <summary>
    /// Criteria for the display name of the item.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Name { get; set; } = new();

    /// <summary>
    /// Criteria for the effective monitoring mode of the item.
    /// </summary>
    public CriteriaSet<PropertyCriterion<MonitoringMode>> Monitoring { get; set; } = new();

    /// <summary>
    /// Whether the item is effectively monitored in any mode, or <see langword="null"/> for no filter.
    /// </summary>
    public bool? EffectiveMonitoring { get; set; }

    /// <summary>
    /// The effective root folder id to match.
    /// </summary>
    public Guid? EffectiveRootFolderId { get; set; }

    /// <summary>
    /// Criteria for the fulfillment status.
    /// </summary>
    public CriteriaSet<PropertyCriterion<FulfillmentStatus>> Fulfillment { get; set; } = new();

    /// <summary>
    /// Criteria for the status of the active acquisition.
    /// </summary>
    public CriteriaSet<PropertyCriterion<DownloadStatus>> ActiveAcquisitionStatus { get; set; } = new();

    /// <summary>
    /// Criteria for the <see cref="QualityStatus"/>.
    /// </summary>
    public CriteriaSet<PropertyCriterion<QualityStatus>> QualityStatus { get; set; } = new();

    /// <summary>
    /// Criteria for the media files of the item. All conditions in one criteria entry apply to the same file.
    /// </summary>
    public CollectionCriteria<MediaFileCriteria> MediaFiles { get; set; } = new();

    /// <summary>
    /// Criteria for the file system path.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Path { get; set; } = new();

    /// <summary>
    /// Criteria for the tags of the item.
    /// </summary>
    public CriteriaSet<PropertyCriterion<string>> Tags { get; set; } = new();

    /// <summary>
    /// Criteria for the metadata of the item.
    /// </summary>
    public CriteriaSet<MetadataCriteria> Metadata { get; set; } = new();

    /// <summary>
    /// Whether the result contains only root-level items.
    /// </summary>
    public bool? RootLevelOnly { get; set; }

    /// <summary>
    /// Whether the result contains only leaf items.
    /// </summary>
    public bool? LeafOnly { get; set; }

    /// <summary>
    /// Whether the result contains only items that await a first metadata refresh.
    /// </summary>
    public bool? PendingMetadataRefresh { get; set; }

    /// <summary>
    /// Whether the result contains only items that a discovered file waits to import into, or only other items.
    /// </summary>
    public bool? ImportPending { get; set; }

    /// <summary>
    /// The time before which the last refresh must have happened, including an item never refreshed.
    /// </summary>
    public DateTime? LastRefreshedBefore { get; set; }

    /// <summary>
    /// Whether the result contains only items with metadata external ids.
    /// </summary>
    public bool? HasMetadataExternalIds { get; set; }

    /// <summary>
    /// Whether the result contains only items with a searchable title.
    /// </summary>
    public bool? HasSearchableTitle { get; set; }

    /// <summary>
    /// Whether the result contains only items with a monitored parent.
    /// </summary>
    public bool? HasMonitoredParent { get; set; }

    /// <summary>
    /// The ids to restrict the result to.
    /// </summary>
    public IReadOnlyCollection<Guid>? Ids { get; set; }

    /// <summary>
    /// The ids to exclude from the result.
    /// </summary>
    public IReadOnlyCollection<Guid>? ExcludeIds { get; set; }

    /// <summary>
    /// The id that every item in the result must follow in the id order, or <see langword="null"/> for no limit.
    /// </summary>
    public Guid? AfterId { get; set; }

    /// <summary>
    /// The id of an ancestor that matches its descendants at any depth.
    /// </summary>
    public Guid? AncestorId { get; set; }
}
