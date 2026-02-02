using System;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Repository interface that supports patch-based updates.
/// </summary>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TModel">The model type.</typeparam>
/// <typeparam name="TPatch">The patch type.</typeparam>
public interface IPatchRepository<TKey, TModel, TPatch> : IRepository<TKey, TModel>
    where TModel : class
    where TKey : notnull
{
    /// <summary>
    /// Patch an entity with partial updates.
    /// </summary>
    /// <param name="patch">The patch object containing the updates.</param>
    /// <param name="modifiedByUserId">The id of the user modifying the record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if the entity was updated, otherwise <see langword="false"/>.
    /// </returns>
    Task<bool> PatchAsync(
        TPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default);
}
