using System;
using System.Collections.Generic;

using Magpie.Models.Metadata;

namespace Magpie.Models.Content;

/// <summary>
/// A level in the content hierarchy.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="ContentHierarchyLevelDefinitionId">The definition this level is based on.</param>
/// <param name="RootFolderId">The root folder this hierarchy belongs to.</param>
/// <param name="Name">The display name.</param>
/// <param name="Slug">The URL-safe identifier.</param>
/// <param name="RelativePath">The path relative to parent.</param>
/// <param name="ParentId">The parent level.</param>
/// <param name="ContentItem">The content item associated with this level.</param>
/// <param name="DateLastRefreshed">When metadata was last refreshed.</param>
/// <param name="Metadata">The metadata container.</param>
/// <param name="LanguagePreferences">Ranked language preferences for this level.</param>
/// <param name="Tags">Tags applied to this hierarchy level.</param>
/// <param name="CreatedAt">When the record was created.</param>
/// <param name="CreatedByUserId">The user who created the record.</param>
/// <param name="ModifiedAt">When the record was last modified.</param>
/// <param name="ModifiedByUserId">The user who last modified the record.</param>
public sealed record ContentHierarchyLevel(
    Guid Id,
    Guid ContentHierarchyLevelDefinitionId,
    Guid RootFolderId,
    string Name,
    string Slug,
    string? RelativePath,
    Guid? ParentId,
    ContentItem? ContentItem,
    DateTimeOffset? DateLastRefreshed,
    MetadataContainer Metadata,
    IReadOnlyList<LanguagePreference> LanguagePreferences,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAt,
    Guid? CreatedByUserId,
    DateTimeOffset? ModifiedAt,
    Guid? ModifiedByUserId);
