using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Result;
using Magpie.Models.Users;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository for managing authentication provider configurations.
/// </summary>
public interface IAuthProviderRepository : ICrudRepository<Guid, AuthProvider>
{
    /// <summary>
    /// Get an authentication provider by name.
    /// </summary>
    /// <param name="name">The provider name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<AuthProvider?>> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all enabled authentication providers for a given provider type.
    /// </summary>
    /// <param name="providerType">The provider type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<AuthProvider>>> GetByProviderTypeAsync(
        string providerType,
        CancellationToken cancellationToken = default);
}
