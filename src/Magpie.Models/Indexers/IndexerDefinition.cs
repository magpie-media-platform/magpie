using System;
using System.Collections.Generic;

namespace Magpie.Models.Indexers;

/// <summary>
/// Definition of an indexer type.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="FeedId">The feed this definition was synced from, if any.</param>
/// <param name="Provider">The provider that handles this indexer.</param>
/// <param name="Name">The display name.</param>
/// <param name="Access">The access type.</param>
/// <param name="Description">The description.</param>
/// <param name="Languages">The languages this indexer has content in.</param>
/// <param name="Categories">The supported categories.</param>
/// <param name="Tags">Tags for filtering and organization.</param>
/// <param name="CreatedAt">When the record was created.</param>
/// <param name="CreatedByUserId">The user who created the record.</param>
/// <param name="ModifiedAt">When the record was last modified.</param>
/// <param name="ModifiedByUserId">The user who last modified the record.</param>
public sealed record IndexerDefinition(
    Guid Id,
    Guid? FeedId,
    string Provider,
    string Name,
    IndexerAccess Access,
    string? Description,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAt,
    Guid? CreatedByUserId,
    DateTimeOffset? ModifiedAt,
    Guid? ModifiedByUserId);
