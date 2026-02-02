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
    /// The content hierarchy item id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the metadata container.
    /// </summary>
    public Guid MetadataContainerId { get; set; }

    /// <summary>
    /// The id of the parent.
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// The id of the quality tier list to use when searching for releases at this item in the hierarchy or below.
    /// </summary>
    public Guid? QualityTierListId { get; set; }

    /// <summary>
    /// The id of the root folder, if the item is a root item.
    /// </summary>
    public Guid? RootFolderId { get; set; }

    /// <summary>
    /// The content type that defines the hierarchy.
    /// </summary>
    public required string ContentType { get; set; }

    /// <summary>
    /// The hierarchy level within the content type.
    /// </summary>
    public required string Level { get; set; }

    /// <summary>
    /// The URL-safe identifier.
    /// </summary>
    public required string Slug { get; set; }

    /// <summary>
    /// The display name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The monitoring intent for this item.
    /// </summary>
    public MonitoringIntent Monitoring { get; set; }

    /// <summary>
    /// The directory path for the item
    /// </summary>
    /// <remarks>
    /// The path may be relative to a parent level, a root folder, or absolute, evaluated in that order.
    /// </remarks>
    public string? Path { get; set; }

    /// <summary>
    /// An ordered list of language preferences for this level.
    /// </summary>
    /// <remarks>
    /// The languages stored as serialized JSON
    /// </remarks>
    public List<string> LanguagePreferences { get; set; } = [];

    /// <summary>
    /// The provider-specific settings for the content hierarchy level.
    /// </summary>
    /// <remarks>
    /// The settings are stored as serialized JSON.
    /// </remarks>
    public Dictionary<string, string> Settings { get; set; } = [];

    /// <summary>
    /// When metadata was last refreshed.
    /// </summary>
    public DateTime? DateLastRefreshed { get; set; }

    /// <summary>
    /// When the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user that created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// When the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user that last modified the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }

    /// <summary>
    /// The <see cref="ContentItem"/> associated with the content hierarchy level.
    /// </summary>
    public ContentItem? ContentItem { get; set; }

    /// <summary>
    /// The <see cref="QualityTierList"/> used for searching.
    /// </summary>
    public QualityTierList? QualityTierList { get; set; }

    /// <summary>
    /// The <see cref="RootFolder"/> this hierarchy belongs to.
    /// </summary>
    public RootFolder? RootFolder { get; set; }

    /// <summary>
    /// The <see cref="MetadataContainer"/> for this level.
    /// </summary>
    public MetadataContainer? MetadataContainer { get; set; }
}
