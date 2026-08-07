using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Paging;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for filtered, sorted, and paged reads.
/// </summary>
/// <typeparam name="TModel">The type of the entity in the repository.</typeparam>
/// <typeparam name="TQuery">The type of the query criteria.</typeparam>
public interface IQueryRepository<TModel, TQuery> : IRepository<TModel> where TModel : class
{
    /// <summary>
    /// Gets one page of <typeparamref name="TModel"/> items.
    /// </summary>
    /// <param name="query">The criteria that an item must match</param>
    /// <param name="paging">The page number and page size</param>
    /// <param name="sort">The field and the direction of the sort, or null for the default order</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>A page of the entities that match</returns>
    Task<PagedList<TModel>> QueryAsync(
        TQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default);
}
