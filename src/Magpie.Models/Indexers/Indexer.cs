using System;
using System.Collections.Generic;

namespace Magpie.Models.Indexers;

/// <summary>
/// A configured indexer instance.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="DefinitionId">The indexer definition this instance is based on.</param>
/// <param name="Name">The display name.</param>
/// <param name="Enabled">Whether the indexer is enabled.</param>
/// <param name="Tags">User-defined tags for filtering and organization.</param>
/// <param name="Settings">Provider-specific settings.</param>
/// <param name="Status">The current health status.</param>
/// <param name="LastChecked">The date of the last health check.</param>
/// <param name="CreatedAt">When the record was created.</param>
/// <param name="CreatedByUserId">The user who created the record.</param>
/// <param name="ModifiedAt">When the record was last modified.</param>
/// <param name="ModifiedByUserId">The user who last modified the record.</param>
public sealed record Indexer(
    Guid Id,
    Guid DefinitionId,
    string Name,
    bool Enabled,
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, string> Settings,
    IndexerStatus Status,
    DateTimeOffset? LastChecked,
    DateTimeOffset CreatedAt,
    Guid? CreatedByUserId,
    DateTimeOffset? ModifiedAt,
    Guid? ModifiedByUserId);
