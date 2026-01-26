using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Result;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Base repository interface for querying entities.
/// </summary>
/// <typeparam name="TModel">The model type.</typeparam>
public interface IRepository<TModel> where TModel : class
{
    /// <summary>
    /// Get all entities.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<TModel>>> GetAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Base repository interface for entities that can be queried by ID.
/// </summary>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TModel">The model type.</typeparam>
public interface IRepository<TKey, TModel> : IRepository<TModel>
    where TKey : notnull
    where TModel : class
{
    /// <summary>
    /// Get an entity by ID.
    /// </summary>
    /// <param name="id">The entity identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<TModel?>> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);
}
