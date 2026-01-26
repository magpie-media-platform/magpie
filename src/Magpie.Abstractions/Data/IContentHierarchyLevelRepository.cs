using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Content;
using Magpie.Models.Filters.Content;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository for managing content hierarchy levels.
/// </summary>
public interface IContentHierarchyLevelRepository :
    ICrudRepository<Guid, ContentHierarchyLevel>,
    IPatchRepository<Guid, ContentHierarchyLevel, ContentHierarchyLevelPatch>
{
    /// <summary>
    /// Get a content hierarchy level by slug.
    /// </summary>
    /// <param name="slug">The slug.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<ContentHierarchyLevel?>> GetBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all content hierarchy levels for a root folder.
    /// </summary>
    /// <param name="rootFolderId">The root folder identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<ContentHierarchyLevel>>> GetByRootFolderIdAsync(
        Guid rootFolderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all content hierarchy levels with a specific parent.
    /// </summary>
    /// <param name="parentId">The parent level identifier, or null for root levels.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<ContentHierarchyLevel>>> GetByParentIdAsync(
        Guid? parentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all content hierarchy levels using a specific definition.
    /// </summary>
    /// <param name="definitionId">The content hierarchy level definition identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<ContentHierarchyLevel>>> GetByDefinitionIdAsync(
        Guid definitionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the last refreshed timestamp for a content hierarchy level.
    /// </summary>
    /// <param name="id">The content hierarchy level identifier.</param>
    /// <param name="dateLastRefreshed">The timestamp of the last refresh.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<bool>> UpdateLastRefreshedAsync(
        Guid id,
        DateTimeOffset dateLastRefreshed,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Query content hierarchy levels with filtering and pagination.
    /// </summary>
    /// <param name="criteria">Filter criteria for content items.</param>
    /// <param name="paging">Pagination parameters, or null for all results.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<PagedResult<ContentHierarchyLevel>>> QueryAsync(
        ContentItemCriteria criteria,
        PagingRequest? paging,
        CancellationToken cancellationToken = default);
}
