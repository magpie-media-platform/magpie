using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The base repository contract for one model type.
/// </summary>
/// <typeparam name="TModel">The type of the entity in the repository.</typeparam>
public interface IRepository<TModel> : IRepository where TModel : class
{
    /// <summary>
    /// Gets every <typeparamref name="TModel"/> entity.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>Every entity</returns>
    Task<IEnumerable<TModel>> ListAllAsync(CancellationToken cancellationToken = default);
}
