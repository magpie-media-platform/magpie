using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Repository for managing user accounts.
/// </summary>
public interface IUserRepository :
    IQueryRepository<User, UserQuery>,
    ICrudRepository<Guid, User>,
    IPatchRepository<Guid, User, UserPatch>
{
    /// <summary>
    /// Get a user by username.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<User?> GetUserAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a user by external id.
    /// </summary>
    /// <param name="providerType">The provider type.</param>
    /// <param name="externalId">The external user id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<User?> GetUserAsync(string providerType, string externalId, CancellationToken cancellationToken = default);
}
