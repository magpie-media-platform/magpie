using System;
using System.Collections.Generic;

namespace Magpie.Sdk.Events;

/// <summary>
/// The routing identities that a content event carries.
/// </summary>
public sealed record ContentEventImpact
{
    /// <summary>
    /// The id of the content hierarchy item that the event is about.
    /// </summary>
    public Guid? HierarchyItemId { get; set; }

    /// <summary>
    /// The id of the content item that the event is about.
    /// </summary>
    public Guid? ItemId { get; set; }

    /// <summary>
    /// The id of the metadata container of the changed hierarchy item.
    /// </summary>
    public Guid? MetadataContainerId { get; set; }

    /// <summary>
    /// The id of the direct parent of the changed hierarchy item.
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// The ids of the ancestor hierarchy items, nearest parent first, up to the root.
    /// </summary>
    public IReadOnlyList<Guid> AncestorIds { get; set; } = [];

    /// <summary>
    /// The id of the top-level ancestor of the changed hierarchy item.
    /// </summary>
    public Guid? RootId { get; set; }

    /// <summary>
    /// The content type slug of the changed hierarchy item.
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// The hierarchy level slug of the changed hierarchy item.
    /// </summary>
    public string? Level { get; set; }

    /// <summary>
    /// Whether the changed hierarchy item has no parent.
    /// </summary>
    public bool? TopLevel { get; set; }

    /// <summary>
    /// The id of the root folder that holds the changed hierarchy item.
    /// </summary>
    public Guid? RootFolderId { get; set; }

    /// <summary>
    /// The id of the quality tier list that the changed hierarchy item resolves to.
    /// </summary>
    public Guid? QualityTierListId { get; set; }

    /// <summary>
    /// Whether the changed hierarchy item is monitored.
    /// </summary>
    public bool? Monitoring { get; set; }

    /// <summary>
    /// The fulfillment status of the changed content item.
    /// </summary>
    public string? Fulfillment { get; set; }

    /// <summary>
    /// The id of the wishlist that requested the changed content item.
    /// </summary>
    public Guid? WishlistId { get; set; }

    /// <summary>
    /// The ids of the hierarchy items removed together with the primary one.
    /// </summary>
    public IReadOnlyList<Guid> SubtreeIds { get; set; } = [];

    /// <summary>
    /// The ids of the hierarchy items that a batch operation affects.
    /// </summary>
    public IReadOnlyList<Guid> BatchIds { get; set; } = [];

    /// <summary>
    /// The id of the metadata container that the changed relationships start from.
    /// </summary>
    public Guid? MetadataSourceContainerId { get; set; }

    /// <summary>
    /// The ids of the metadata containers that the source container gains a relationship to.
    /// </summary>
    public IReadOnlyList<Guid> MetadataAddedContainerIds { get; set; } = [];

    /// <summary>
    /// The ids of the metadata containers that the source container loses a relationship to.
    /// </summary>
    public IReadOnlyList<Guid> MetadataRemovedContainerIds { get; set; } = [];

    /// <summary>
    /// The scope that the changed entity occupied before the change.
    /// </summary>
    public ContentEventImpact? Previous { get; set; }
}
