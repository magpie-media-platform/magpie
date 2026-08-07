using System.Collections.Generic;

namespace Magpie.Models.Data;

/// <summary>
/// A label that groups records of different kinds.
/// </summary>
public record Tag
{
    /// <summary>
    /// The name of the tag to show.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The download clients with the tag.
    /// </summary>
    public ICollection<DownloadClient> DownloadClients { get; } = [];

    /// <summary>
    /// The indexer connections with the tag.
    /// </summary>
    public ICollection<IndexerConnection> IndexerConnections { get; } = [];

    /// <summary>
    /// The media servers with the tag.
    /// </summary>
    public ICollection<MediaServer> MediaServers { get; } = [];

    /// <summary>
    /// The metadata containers with the tag.
    /// </summary>
    public ICollection<MetadataContainer> MetadataContainers { get; } = [];

    /// <summary>
    /// The subject filters with the tag.
    /// </summary>
    public ICollection<SubjectFilter> SubjectFilters { get; } = [];

    /// <summary>
    /// The content filters with the tag.
    /// </summary>
    public ICollection<ContentFilter> ContentFilters { get; } = [];

    /// <summary>
    /// The quality tier lists with the tag.
    /// </summary>
    public ICollection<QualityTierList> QualityTierLists { get; } = [];

    /// <summary>
    /// The releases with the tag.
    /// </summary>
    public ICollection<Release> Releases { get; } = [];

    /// <summary>
    /// The wishlists with the tag.
    /// </summary>
    public ICollection<Wishlist> Wishlists { get; } = [];
}
