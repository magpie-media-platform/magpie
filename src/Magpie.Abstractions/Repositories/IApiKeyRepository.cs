using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for API keys.
/// </summary>
public interface IApiKeyRepository :
    IQueryRepository<ApiKey, ApiKeyQuery>,
    ICrudRepository<Guid, ApiKey>,
    IPatchRepository<Guid, ApiKey, ApiKeyPatch>
{
    /// <summary>
    /// Updates only the last use time of an API key.
    /// </summary>
    /// <param name="id">The API key id</param>
    /// <param name="lastUsedAt">The UTC time of the use</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task UpdateLastUsedAsync(Guid id, DateTime lastUsedAt, CancellationToken cancellationToken = default);
}
