using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Repositories;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for user accounts.
/// </summary>
public interface IUserRepository :
    IQueryRepository<User, UserQuery>,
    ICrudRepository<Guid, User>,
    IPatchRepository<Guid, User, UserPatch>
{
    /// <summary>
    /// Replaces the stored password hash and salt of a user.
    /// </summary>
    /// <param name="userId">The id of the user</param>
    /// <param name="passwordHash">The new hash of the password</param>
    /// <param name="passwordSalt">The new salt of the password</param>
    /// <param name="modifiedByUserId">The id of the user that changes the record, or <c>null</c> when none</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> when the user exists, otherwise <see langword="false"/></returns>
    Task<bool> SetPasswordAsync(
        Guid userId,
        string passwordHash,
        string passwordSalt,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds one to the failed login counter of a user and applies a lockout, in a single atomic step.
    /// </summary>
    /// <param name="userId">The id of the user</param>
    /// <param name="lockedUntil">The end of the lockout, or <c>null</c> to keep the stored value</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The failed-login state with its new values</returns>
    Task<FailedLoginResult> RecordFailedLoginAsync(
        Guid userId,
        DateTime? lockedUntil,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets the failed login counter of a user.
    /// </summary>
    /// <param name="userId">The id of the user</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task ClearFailedLoginsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the end of the lockout of a user.
    /// </summary>
    /// <param name="userId">The id of the user</param>
    /// <param name="modifiedByUserId">The id of the user that changes the record, or <c>null</c> when none</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> when the user exists, otherwise <see langword="false"/></returns>
    Task<bool> ResetLockoutAsync(
        Guid userId,
        Guid? modifiedByUserId = null,
        CancellationToken cancellationToken = default);
}
