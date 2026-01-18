using System;
using System.Collections.Generic;

namespace Magpie.Models.Indexers;

/// <summary>
/// A release from an indexer.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="IndexerId">The indexer this release came from.</param>
/// <param name="ExternalId">The indexer's identifier for this release.</param>
/// <param name="Provider">The provider that supplied this release.</param>
/// <param name="Title">The release title.</param>
/// <param name="PublishDate">The publication date.</param>
/// <param name="Size">The release size in bytes.</param>
/// <param name="InfoHash">The torrent info hash, if applicable.</param>
/// <param name="Categories">The categories this release belongs to.</param>
/// <param name="DownloadUrl">The download URL, magnet URI, or NZB link.</param>
/// <param name="InfoUrl">The URL for release information.</param>
/// <param name="Properties">Provider-specific properties.</param>
/// <param name="Languages">Languages available in this release.</param>
/// <param name="Tags">Tags applied to this release.</param>
/// <param name="CreatedAt">When the record was created.</param>
/// <param name="CreatedByUserId">The user who created the record.</param>
/// <param name="ModifiedAt">When the record was last modified.</param>
/// <param name="ModifiedByUserId">The user who last modified the record.</param>
public sealed record Release(
    Guid Id,
    Guid? IndexerId,
    string? ExternalId,
    string Provider,
    string Title,
    DateTimeOffset PublishDate,
    long? Size,
    string? InfoHash,
    IReadOnlyList<string> Categories,
    Uri DownloadUrl,
    Uri? InfoUrl,
    IReadOnlyDictionary<string, string>? Properties,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAt,
    Guid? CreatedByUserId,
    DateTimeOffset? ModifiedAt,
    Guid? ModifiedByUserId);
