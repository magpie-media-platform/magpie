using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Repository for managing content hierarchy level items.
/// </summary>
public interface IContentHierarchyItemRepository :
    ICrudRepository<Guid, ContentHierarchyItem>,
    IQueryRepository<ContentHierarchyItem, ContentHierarchyItemCriteria>,
    IPatchRepository<Guid, ContentHierarchyItem, ContentHierarchyItemPatch>
{
    /// <summary>
    /// Get a content hierarchy level item by slug.
    /// </summary>
    /// <param name="contentType">The content type slug.</param>
    /// <param name="level">The hierarchy level slug.</param>
    /// <param name="slug">The hierarchy item slug.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<ContentHierarchyItem?> GetBySlugAsync(
        string contentType,
        string level,
        string slug,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compute aggregate fulfillment counts across the entire content library.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The aggregate <see cref="ContentStats"/>.</returns>
    Task<ContentStats> GetStatsAsync(CancellationToken cancellationToken = default);
}
