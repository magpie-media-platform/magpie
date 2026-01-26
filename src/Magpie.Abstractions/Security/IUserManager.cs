using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Result;
using Magpie.Models.Users;

namespace Magpie.Abstractions.Security;

/// <summary>
/// Provides user management services including credential verification and password management.
/// </summary>
public interface IUserManager
{
    /// <summary>
    /// Retrieves users, optionally filtered by enabled status.
    /// </summary>
    /// <param name="authProviderId">Optionally filter by auth provider.</param>
    /// <param name="role">Optionally filter by role.</param>
    /// <param name="enabled">Optionally filter by enabled status.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IEnumerable<User>>> GetUsersAsync(
        Guid? authProviderId = null,
        Role? role = null,
        bool? enabled = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a user by user id.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<User?>> GetUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a user by username.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<User?>> GetUserByUsernameAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a user by external ID.
    /// </summary>
    /// <param name="authProviderId">The auth provider identifier.</param>
    /// <param name="externalId">The external user identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<User?>> GetUserByExternalIdAsync(
        Guid authProviderId,
        string externalId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new user.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="email">The email address.</param>
    /// <param name="password">Optional password for local authentication.</param>
    /// <param name="role">The role to assign.</param>
    /// <param name="enabled">Whether the user account is enabled.</param>
    /// <param name="createdByUserId">The id of the user creating the record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<User>> CreateLocalUserAsync(
        string username,
        string? email,
        string password,
        Role role,
        bool enabled,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new external user.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="email">The email address.</param>
    /// <param name="authProviderId">The auth provider id.</param>
    /// <param name="externalId">An external user identifier.</param>
    /// <param name="role">The role to assign.</param>
    /// <param name="enabled">Whether the user account is enabled.</param>
    /// <param name="providerData">The provider-specific data.</param>
    /// <param name="createdByUserId">The id of the user creating the record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<User>> CreateExternalUserAsync(
         string username,
         string? email,
         Guid authProviderId,
         string? externalId,
         Role role,
         bool enabled,
         IReadOnlyDictionary<string, string> providerData,
         Guid? createdByUserId,
         CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a user.
    /// </summary>
    /// <param name="userPatch">The patch containing fields to update.</param>
    /// <param name="modifiedByUserId">The id of the user modifying the record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<User>> UpdateUserAsync(
        UserPatch userPatch,
        Guid modifiedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a user.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="deletedByUserId">The id of the user deleting the record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the user was deleted, <see langword="false"/> otherwise.</returns>
    Task<Result<bool>> DeleteUserAsync(
        Guid userId,
        Guid? deletedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a user's password credentials. Automatically records successful/failed login attempts.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="password">The plaintext password to verify.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The user if credentials are valid, null if user not found or password invalid.</returns>
    /// <remarks>
    /// This method handles updating failed login attempts and locking the user account as necessary.
    /// </remarks>
    Task<Result<User?>> VerifyCredentialsAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a user's password.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="password">The plaintext password to set.</param>
    /// <param name="modifiedByUserId">The id of the user modifying the record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<Unit>> ResetPasswordAsync(
        Guid userId,
        string password,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a ClaimsPrincipal for the specified user.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<ClaimsPrincipal>> CreatePrincipalAsync(User user, CancellationToken cancellationToken = default);
}
