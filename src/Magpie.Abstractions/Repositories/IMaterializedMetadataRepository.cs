using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;
using Magpie.Models.Paging;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Repository for querying materialized metadata.
/// </summary>
public interface IMaterializedMetadataRepository : IQueryRepository<MaterializedMetadata, MetadataCriteria>
{
    /// <summary>
    /// List all <see cref="MaterializedMetadata"/> with optional language preference.
    /// </summary>
    /// <param name="preferredLanguage">
    /// When specified, contributions in this language are prioritized for scalar fields.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IEnumerable<MaterializedMetadata>> ListAllAsync(
        string? preferredLanguage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Query <see cref="MaterializedMetadata"/> with optional language preference.
    /// </summary>
    /// <param name="query">The query criteria.</param>
    /// <param name="paging">The paging parameters.</param>
    /// <param name="sort">The sort parameters.</param>
    /// <param name="preferredLanguage">
    /// When specified, contributions in this language are prioritized for scalar fields.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<PagedList<MaterializedMetadata>> QueryAsync(
        MetadataCriteria query,
        PagingParameter? paging,
        SortParameter? sort,
        string? preferredLanguage,
        CancellationToken cancellationToken = default);
}
