using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Filters.Metadata;
using Magpie.Models.Metadata;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository for materializing metadata and querying materialized views.
/// </summary>
public interface IMaterializedMetadataRepository
{
    /// <summary>
    /// Materialize metadata for a container by applying layered contribution rules.
    /// </summary>
    /// <param name="containerId">The metadata container identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<MaterializedMetadata?>> MaterializeAsync(
        Guid containerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a queryable projection of materialized metadata.
    /// </summary>
    /// <returns>An <see cref="IQueryable{T}"/> of <see cref="MaterializedProjection"/>.</returns>
    IQueryable<MaterializedProjection> GetMaterializedQuery();

    /// <summary>
    /// Get an IQueryable of container IDs that match the specified criteria.
    /// </summary>
    /// <remarks>
    /// Filters are applied to the materialized scalar fields (first non-null by ordinal).
    /// The query remains composable and is not executed until enumerated.
    /// </remarks>
    /// <param name="criteria">Filter criteria for materialized metadata.</param>
    IQueryable<Guid> GetMatchingContainerIds(MetadataContributionCriteria criteria);

    /// <summary>
    /// Query materialized metadata without concern for parent entity type.
    /// </summary>
    /// <param name="criteria">Filter criteria for metadata contributions.</param>
    /// <param name="paging">Pagination parameters, or null for all results.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<PagedResult<MaterializedMetadataResult>>> QueryMaterializedAsync(
        MetadataContributionCriteria criteria,
        PagingRequest? paging,
        CancellationToken cancellationToken = default);
}
