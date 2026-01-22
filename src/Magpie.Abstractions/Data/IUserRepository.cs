using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Result;
using Magpie.Models.Users;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository for managing user accounts.
/// </summary>
public interface IUserRepository :
    ICrudRepository<Guid, User>,
    IPatchRepository<Guid, User, UserPatch>
{
    /// <summary>
    /// Get a user by username.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<User?>> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a user by external ID from an auth provider.
    /// </summary>
    /// <param name="authProviderId">The auth provider identifier.</param>
    /// <param name="externalId">The external user identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<User?>> GetByExternalIdAsync(
        Guid authProviderId,
        string externalId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a user's password credentials.
    /// </summary>
    /// <param name="id">The user identifier.</param>
    /// <param name="passwordHash">The new password hash.</param>
    /// <param name="passwordSalt">The new password salt.</param>
    /// <param name="modifiedByUserId">The user who is modifying this record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result> UpdatePasswordAsync(
        Guid id,
        string passwordHash,
        string passwordSalt,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Record a failed login attempt.
    /// </summary>
    /// <param name="id">The user identifier.</param>
    /// <param name="lockoutDuration">Optional duration to lock the account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result> RecordFailedLoginAsync(
        Guid id,
        TimeSpan? lockoutDuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Record a successful login.
    /// </summary>
    /// <param name="id">The user identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result> RecordSuccessfulLoginAsync(Guid id, CancellationToken cancellationToken = default);
}
