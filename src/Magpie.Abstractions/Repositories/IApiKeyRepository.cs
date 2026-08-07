using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for API keys.
/// </summary>
public interface IApiKeyRepository :
    IQueryRepository<ApiKey, ApiKeyQuery>,
    ICrudRepository<Guid, ApiKey>,
    IPatchRepository<Guid, ApiKey, ApiKeyPatch>;
