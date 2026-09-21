using System.Collections.Generic;

using Magpie.Models.Data;

namespace Magpie.Sdk.Diagnostics;

/// <summary>
/// The configured host entities that a diagnostic check reads.
/// </summary>
public sealed record DiagnosticContext
{
    /// <summary>
    /// A context with no configured entities.
    /// </summary>
    public static DiagnosticContext Empty => new();

    /// <summary>
    /// The configured media servers.
    /// </summary>
    public IReadOnlyList<MediaServer> MediaServers { get; set; } = [];

    /// <summary>
    /// The configured root folders.
    /// </summary>
    public IReadOnlyList<RootFolder> RootFolders { get; set; } = [];

    /// <summary>
    /// The configured download clients.
    /// </summary>
    public IReadOnlyList<DownloadClient> DownloadClients { get; set; } = [];

    /// <summary>
    /// The configured indexer feeds.
    /// </summary>
    public IReadOnlyList<IndexerFeed> IndexerFeeds { get; set; } = [];
}
