using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Result;
using Magpie.Models.Users;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository for managing user accounts.
/// </summary>
public interface IUserRepository : IPatchRepository<Guid, User, UserPatch>
{
    /// <summary>
    /// Get a user by username.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<User?>> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a user by external ID from an auth provider.
    /// </summary>
    /// <param name="authProviderId">The auth provider identifier.</param>
    /// <param name="externalId">The external user identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<User?>> GetByExternalIdAsync(
        Guid authProviderId,
        string externalId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the stored credentials for a user by username.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<UserCredentials?>> GetUserCredentialsAsync(
        string username,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new external user.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="email">The email address.</param>
    /// <param name="authProviderId">The auth provider id.</param>
    /// <param name="externalId">An external user identifier.</param>
    /// <param name="enabled">Whether the user is enabled.</param>
    /// <param name="role">The user's role.</param>
    /// <param name="passwordHash">The password hash.</param>
    /// <param name="passwordSalt">The password salt.</param>
    /// <param name="providerData">The provider-specific data.</param>
    /// <param name="createdAt">The creation timestamp.</param>
    /// <param name="createdByUserId">The id of the user creating the record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<User>> CreateAsync(
        string username,
        string email,
        Guid authProviderId,
        string? externalId,
        bool enabled,
        Role role,
        string? passwordHash,
        string? passwordSalt,
        IReadOnlyDictionary<string, string> providerData,
        DateTimeOffset createdAt,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a user's password credentials.
    /// </summary>
    /// <param name="id">The user identifier.</param>
    /// <param name="passwordHash">The new password hash.</param>
    /// <param name="passwordSalt">The new password salt.</param>
    /// <param name="unlock">Whether to unlock the user account.</param>
    /// <param name="resetFailedLoginAttempts">Whether to reset the failed login attempts counter.</param>
    /// <param name="modifiedByUserId">The id of the user modifying the record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<Unit>> UpdatePasswordAsync(
        Guid id,
        string? passwordHash,
        string? passwordSalt,
        bool? unlock,
        bool? resetFailedLoginAttempts,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unlock a user account.
    /// </summary>
    /// <param name="id">The user identifier.</param>
    /// <param name="resetFailedLoginAttempts">Whether to reset the failed login attempts counter.</param>
    /// <param name="modifiedByUserId">The id of the user modifying the record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<User>> UnlockUserAsync(
        Guid id,
        bool? resetFailedLoginAttempts,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Record a failed login attempt.
    /// </summary>
    /// <param name="id">The user identifier.</param>
    /// <param name="lockoutDuration">Optional duration to lock the account.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<Unit>> RecordFailedLoginAsync(
        Guid id,
        TimeSpan? lockoutDuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reset the failed login attempts counter.
    /// </summary>
    /// <param name="id">The user identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<Unit>> ResetFailedLoginAttemptsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a user by ID.
    /// </summary>
    /// <param name="id">The user id.</param>
    /// <param name="deletedByUserId">The id of the user performing the deletion.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<bool>> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken = default);
}
