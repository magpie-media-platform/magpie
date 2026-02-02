using System;
using System.Threading;
using System.Threading.Tasks;


namespace Magpie.Abstractions.Repositories;

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
    /// <param name="createdByUserId">The id of the user creating the record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<TModel> CreateAsync(
        TModel model,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing entity.
    /// </summary>
    /// <param name="model">The model with updated values.</param>
    /// <param name="modifiedByUserId">The id of the user modifying the record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the entity was updated, otherwise <see langword="false"/>.</returns>
    Task<bool> UpdateAsync(
        TModel model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete an entity by id.
    /// </summary>
    /// <param name="id">The entity id.</param>
    /// <param name="deletedByUserId">The id of the user deleting the record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the entity was deleted, otherwise <see langword="false"/>.</returns>
    Task<bool> DeleteAsync(
        TKey id,
        Guid? deletedByUserId,
        CancellationToken cancellationToken = default);
}
