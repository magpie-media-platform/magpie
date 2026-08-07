using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Bulk;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for bulk updates.
/// </summary>
/// <typeparam name="TKey">The type of the entity key.</typeparam>
/// <typeparam name="TModel">The type of the entity in the repository.</typeparam>
/// <typeparam name="TFilter">The type of the query filter for the domain.</typeparam>
/// <typeparam name="TPatch">The type of the patch.</typeparam>
public interface IBulkPatchRepository<TKey, TModel, TFilter, TPatch> : IKeyedRepository<TKey, TModel>
    where TModel : class
    where TKey : notnull
    where TFilter : class
    where TPatch : class
{
    /// <summary>
    /// Updates every entity that the bulk selection matches.
    /// </summary>
    /// <param name="patch">The selection of entities and the new field values</param>
    /// <param name="modifiedByUserId">The id of the user that changes the records</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<BulkResult> BulkPatchAsync(
        BulkPatch<TFilter, TPatch> patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default);
}
