using System;

namespace Magpie.Models.Integrations;

/// <summary>
/// A media server deep link for a content item.
/// </summary>
public sealed record MediaServerDeepLink
{
    /// <summary>
    /// The id of the media server.
    /// </summary>
    public required Guid MediaServerId { get; set; }

    /// <summary>
    /// The name of the media server.
    /// </summary>
    public required string ServerName { get; set; }

    /// <summary>
    /// The provider type of the media server.
    /// </summary>
    public required string ProviderType { get; set; }

    /// <summary>
    /// The deep-link URL.
    /// </summary>
    public required string Url { get; set; }
}
