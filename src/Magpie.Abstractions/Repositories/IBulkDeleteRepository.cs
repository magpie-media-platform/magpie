using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for deleting entities that match a query.
/// </summary>
/// <typeparam name="TModel">The entity type.</typeparam>
/// <typeparam name="TQuery">The query type.</typeparam>
public interface IBulkDeleteRepository<TModel, in TQuery> : IRepository<TModel> where TModel : class
{
    /// <summary>
    /// Deletes the entities that match a query.
    /// </summary>
    /// <param name="query">The criteria that an entity must match</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<int> DeleteWhereAsync(TQuery query, CancellationToken cancellationToken = default);
}
