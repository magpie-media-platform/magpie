using System;
using System.Collections.Generic;

namespace Magpie.Models.Indexers;

/// <summary>
/// Configuration for an indexer feed.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="Name">The display name.</param>
/// <param name="Description">The feed description.</param>
/// <param name="Provider">The feed provider that handles this feed.</param>
/// <param name="Enabled">Whether the feed is enabled.</param>
/// <param name="Settings">Provider-specific settings.</param>
/// <param name="LastSync">The date of the last successful sync.</param>
/// <param name="CreatedAt">When the record was created.</param>
/// <param name="CreatedByUserId">The user who created the record.</param>
/// <param name="ModifiedAt">When the record was last modified.</param>
/// <param name="ModifiedByUserId">The user who last modified the record.</param>
public sealed record IndexerFeed(
    Guid Id,
    string Name,
    string? Description,
    string Provider,
    bool Enabled,
    IReadOnlyDictionary<string, string?> Settings,
    DateTimeOffset? LastSync,
    DateTimeOffset CreatedAt,
    Guid? CreatedByUserId,
    DateTimeOffset? ModifiedAt,
    Guid? ModifiedByUserId);
