using System;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for create, update, and delete operations.
/// </summary>
/// <typeparam name="TKey">The type of the entity key.</typeparam>
/// <typeparam name="TModel">The type of the entity in the repository.</typeparam>
public interface ICrudRepository<TKey, TModel> : IKeyedRepository<TKey, TModel>
    where TKey : notnull
    where TModel : class
{
    /// <summary>
    /// Creates a new entity.
    /// </summary>
    /// <param name="model">The entity to create</param>
    /// <param name="createdByUserId">
    /// The id of the user that creates the record, or <c>null</c> when no user started it.
    /// </param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The new entity</returns>
    Task<TModel> CreateAsync(
        TModel model,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a stored entity.
    /// </summary>
    /// <param name="model">The entity with its new values</param>
    /// <param name="modifiedByUserId">
    /// The id of the user that changes the record, or <c>null</c> when no user started it.
    /// </param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> when the entity exists, otherwise <see langword="false"/></returns>
    Task<bool> UpdateAsync(
        TModel model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an entity by id.
    /// </summary>
    /// <param name="id">The id of the entity to delete</param>
    /// <param name="deletedByUserId">
    /// The id of the user that deletes the record, or <c>null</c> when no user started it.
    /// </param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> when the entity exists, otherwise <see langword="false"/></returns>
    Task<bool> DeleteAsync(
        TKey id,
        Guid? deletedByUserId,
        CancellationToken cancellationToken = default);
}
