using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Result;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Base repository interface for entities with basic CRUD operations.
/// </summary>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TModel">The model type.</typeparam>
public interface ICrudRepository<TKey, TModel> : IRepository<TKey, TModel>
    where TKey : notnull
    where TModel : class
{
    /// <summary>
    /// Create a new entity.
    /// </summary>
    /// <param name="model">The model to create.</param>
    /// <param name="createdByUserId">The user who is creating this record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<TModel>> CreateAsync(
        TModel model,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing entity.
    /// </summary>
    /// <param name="model">The model with updated values.</param>
    /// <param name="modifiedByUserId">The user who is modifying this record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> containing <see langword="true"/> if the entity was updated,
    /// otherwise <see langword="false"/>.
    /// </returns>
    Task<Result<bool>> UpdateAsync(
        TModel model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete an entity by ID.
    /// </summary>
    /// <param name="id">The entity identifier.</param>
    /// <param name="deletedByUserId">The user who is deleting this record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> containing <see langword="true"/> if the entity was deleted,
    /// otherwise <see langword="false"/>.
    /// </returns>
    Task<Result<bool>> DeleteAsync(
        TKey id,
        Guid? deletedByUserId,
        CancellationToken cancellationToken = default);
}
