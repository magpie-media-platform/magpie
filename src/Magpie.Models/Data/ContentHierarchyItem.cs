using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Data;

/// <summary>
/// An item in the content hierarchy.
/// </summary>
public record ContentHierarchyItem
{
    /// <summary>
    /// The unique id of the content hierarchy item.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the metadata container of the item.
    /// </summary>
    public Guid MetadataContainerId { get; set; }

    /// <summary>
    /// The id of the parent item.
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// The id of the quality tier list of the item, or <see langword="null"/> to inherit.
    /// </summary>
    public Guid? QualityTierListId { get; set; }

    /// <summary>
    /// The id of the root folder of the item, or <see langword="null"/> to inherit.
    /// </summary>
    public Guid? RootFolderId { get; set; }

    /// <summary>
    /// The slug of the content type.
    /// </summary>
    public required string ContentType { get; set; }

    /// <summary>
    /// The level of the item in the content hierarchy.
    /// </summary>
    public required string Level { get; set; }

    /// <summary>
    /// The stable slug of the item.
    /// </summary>
    public required string Slug { get; set; }

    /// <summary>
    /// The display name of the item.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The monitoring mode of the item, or <see langword="null"/> to inherit.
    /// </summary>
    public MonitoringMode? Monitoring { get; set; }

    /// <summary>
    /// The monitoring mode resolved from this item up the ancestor chain.
    /// </summary>
    public MonitoringMode EffectiveMonitoring { get; set; }

    /// <summary>
    /// The root folder id resolved from this item up the ancestor chain.
    /// </summary>
    public Guid EffectiveRootFolderId { get; set; }

    /// <summary>
    /// The quality tier list id resolved from this item up the ancestor chain, or <see langword="null"/> for none.
    /// </summary>
    public Guid? EffectiveQualityTierListId { get; set; }

    /// <summary>
    /// The position in the display order.
    /// </summary>
    public int? Ordinal { get; set; }

    /// <summary>
    /// The stable key that matches this item with an existing sibling.
    /// </summary>
    public string? AlignmentKey { get; set; }

    /// <summary>
    /// The file system path of the item.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// The language codes of the item, in order of preference.
    /// </summary>
    public List<string> LanguagePreferences { get; set; } = [];

    /// <summary>
    /// The settings of the item, keyed by name.
    /// </summary>
    public Dictionary<string, string> Settings { get; set; } = [];

    /// <summary>
    /// The UTC time of the last metadata refresh.
    /// </summary>
    public DateTime? DateLastRefreshed { get; set; }

    /// <summary>
    /// Whether the item awaits its first successful metadata refresh.
    /// </summary>
    public bool PendingMetadataRefresh { get; set; }

    /// <summary>
    /// Whether a discovered file waits to import into the item, derived from the import candidates when read.
    /// </summary>
    public bool ImportPending { get; set; }

    /// <summary>
    /// The names of the tags on the item, or <see langword="null"/> when the read did not load them.
    /// </summary>
    public List<string>? Tags { get; set; }

    /// <summary>
    /// The UTC time the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user who created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// The UTC time the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user who last modified the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }

    /// <summary>
    /// The related <see cref="ContentItem"/>.
    /// </summary>
    public ContentItem? ContentItem { get; set; }

    /// <summary>
    /// The related <see cref="QualityTierList"/>.
    /// </summary>
    public QualityTierList? QualityTierList { get; set; }

    /// <summary>
    /// The related <see cref="RootFolder"/>.
    /// </summary>
    public RootFolder? RootFolder { get; set; }

    /// <summary>
    /// The related <see cref="MetadataContainer"/>.
    /// </summary>
    public MetadataContainer? MetadataContainer { get; set; }
}
