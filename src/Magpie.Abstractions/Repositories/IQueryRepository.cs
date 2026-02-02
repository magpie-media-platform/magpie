using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Paging;
using Magpie.Models.Sorting;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Repository interface that support filtering, sorting, and paging.
/// </summary>
public interface IQueryRepository<TModel, TQuery> : IRepository<TModel> where TModel : class
{
    /// <summary>
    /// Get a page of <see typeparamref="TModel"/> items.
    /// </summary>
    /// <param name="query">The query criteria.</param>
    /// <param name="paging">The paging parameters.</param>
    /// <param name="sort">The sort parameters.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<PagedList<TModel>> QueryAsync(
        TQuery query,
        PagingParameter? paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default);
}
