using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Result;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository interface that supports patch-based updates.
/// </summary>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TModel">The model type.</typeparam>
/// <typeparam name="TPatch">The patch type.</typeparam>
public interface IPatchRepository<TKey, TModel, TPatch> : IRepository<TKey, TModel>
    where TKey : notnull
    where TModel : class
    where TPatch : class
{
    /// <summary>
    /// Patch an entity with partial updates.
    /// </summary>
    /// <param name="patch">The patch object containing the updates.</param>
    /// <param name="modifiedByUserId">The user who is modifying this record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> containing <see langword="true"/> if the entity was patched,
    /// otherwise <see langword="false"/>.
    /// </returns>
    Task<Result<bool>> PatchAsync(
        TPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default);
}
