using System.Collections.Generic;

namespace Magpie.Models.Data;

/// <summary>
/// A tag for categorizing entities.
/// </summary>
public record Tag
{
    /// <summary>
    /// The tag name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The <see cref="DownloadClient"/>s associated with this tag.
    /// </summary>
    public ICollection<DownloadClient> DownloadClients { get; } = [];

    /// <summary>
    /// The <see cref="IndexerConnection"/>s associated with this tag.
    /// </summary>
    public ICollection<IndexerConnection> IndexerConnections { get; } = [];

    /// <summary>
    /// The <see cref="MediaServer"/>s associated with this tag.
    /// </summary>
    public ICollection<MediaServer> MediaServers { get; } = [];

    /// <summary>
    /// The <see cref="MetadataContainer"/>s associated with this tag.
    /// </summary>
    public ICollection<MetadataContainer> MetadataContainers { get; } = [];

    /// <summary>
    /// The <see cref="SubjectFilter"/>s associated with this tag.
    /// </summary>
    public ICollection<SubjectFilter> SubjectFilters { get; } = [];

    /// <summary>
    /// The <see cref="ContentFilter"/>s associated with this tag.
    /// </summary>
    public ICollection<ContentFilter> ContentFilters { get; } = [];

    /// <summary>
    /// The <see cref="QualityTierList"/>s associated with this tag.
    /// </summary>
    public ICollection<QualityTierList> QualityTierLists { get; } = [];

    /// <summary>
    /// The <see cref="Release"/>s associated with this tag.
    /// </summary>
    public ICollection<Release> Releases { get; } = [];

    /// <summary>
    /// The <see cref="Wishlist"/>s associated with this tag.
    /// </summary>
    public ICollection<Wishlist> Wishlists { get; } = [];
}
