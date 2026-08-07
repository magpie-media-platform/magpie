using System;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for partial updates.
/// </summary>
/// <typeparam name="TKey">The type of the entity key.</typeparam>
/// <typeparam name="TModel">The type of the entity in the repository.</typeparam>
/// <typeparam name="TPatch">The type of the patch.</typeparam>
public interface IPatchRepository<TKey, TModel, TPatch> : IKeyedRepository<TKey, TModel>
    where TModel : class
    where TKey : notnull
{
    /// <summary>
    /// Updates only the fields that a patch sets.
    /// </summary>
    /// <param name="patch">The patch that names the entity and the new field values</param>
    /// <param name="modifiedByUserId">The id of the user that changes the record</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>
    /// <see langword="true"/> when the entity exists, otherwise <see langword="false"/>.
    /// </returns>
    Task<bool> PatchAsync(
        TPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default);
}
