using System.Collections.Generic;
using System.Linq;
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
    private const int DrainPageSize = 500;

    /// <summary>
    /// Queries one page of <typeparamref name="TModel"/> items.
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

    /// <summary>
    /// Reads every <typeparamref name="TModel"/> item that matches, one page at a time.
    /// </summary>
    /// <param name="query">The criteria that an item must match</param>
    /// <param name="sort">The field and the direction of the sort, or null for the default order</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>Every entity that matches</returns>
    async Task<IReadOnlyList<TModel>> QueryAllAsync(
        TQuery query,
        SortParameter? sort = null,
        CancellationToken cancellationToken = default)
    {
        var items = new List<TModel>();
        for (var pageNumber = 1; ; pageNumber++)
        {
            var page = await QueryAsync(
                query,
                new() { PageNumber = pageNumber, PageSize = DrainPageSize },
                sort,
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            items.AddRange(pageItems);
            if (pageNumber * DrainPageSize >= page.TotalItems || pageItems.Count == 0)
            {
                return items;
            }
        }
    }
}
