using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The base repository contract for entities that have an id.
/// </summary>
/// <typeparam name="TKey">The type of the entity key.</typeparam>
/// <typeparam name="TModel">The type of the entity in the repository.</typeparam>
public interface IKeyedRepository<TKey, TModel> : IRepository<TModel>
    where TKey : notnull
    where TModel : class
{
    /// <summary>
    /// Gets an entity by id.
    /// </summary>
    /// <param name="id">The id of the entity to get</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The entity, or <see langword="null"/> when the id is unknown</returns>
    Task<TModel?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);
}
