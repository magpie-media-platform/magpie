using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Paging;
using Magpie.Models.Sorting;

namespace Magpie.Abstractions.Security;

/// <summary>
/// Provides user management services including credential verification and password management.
/// </summary>
public interface IUserManager
{
    /// <summary>
    /// Query users.
    /// </summary>
    Task<PagedList<User>> QueryAsync(
        UserQuery query,
        SortParameter? sort = null,
        PagingParameter? paging = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve a user by user id.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<User?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve a user by username.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<User?> GetUserAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve a user by external id.
    /// </summary>
    /// <param name="providerType">The provider type.</param>
    /// <param name="externalId">The external user id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<User?> GetUserAsync(
        string providerType,
        string externalId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new user.
    /// </summary>
    Task<User> CreateUserAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new local user.
    /// </summary>
    /// <param name="createArgs">The arguments for creating the local user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<User> CreateLocalUserAsync(UserCreate createArgs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a user.
    /// </summary>
    /// <param name="userPatch">The patch containing fields to update.</param>
    /// <param name="modifiedByUserId">The id of the user modifying the record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<User> UpdateUserAsync(
        UserPatch userPatch,
        Guid modifiedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a user.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="deletedByUserId">The id of the user deleting the record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the user was deleted, <see langword="false"/> otherwise.</returns>
    Task<bool> DeleteUserAsync(
        Guid userId,
        Guid? deletedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a ClaimsPrincipal for the specified user.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<ClaimsPrincipal> CreatePrincipalAsync(User user, CancellationToken cancellationToken = default);
}
