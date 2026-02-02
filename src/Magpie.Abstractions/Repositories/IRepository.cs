using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;


namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Base repository interface for querying entities.
/// </summary>
/// <typeparam name="TModel">The model type.</typeparam>
public interface IRepository<TModel> where TModel : class
{
    /// <summary>
    /// List all <typeparamref name="TModel"/> entities.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IEnumerable<TModel>> ListAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Base repository interface for entities that can be queried by id.
/// </summary>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TModel">The model type.</typeparam>
public interface IRepository<TKey, TModel> : IRepository<TModel>
    where TKey : notnull
    where TModel : class
{
    /// <summary>
    /// Get an entity by id.
    /// </summary>
    /// <param name="id">The entity id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The entity if found, otherwise <see langword="null"/>.</returns>
    Task<TModel?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);
}
