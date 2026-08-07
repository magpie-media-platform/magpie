using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Paging;

namespace Magpie.Sdk.Services;

/// <summary>
/// The service contract for users, their profiles, and their passwords.
/// </summary>
public interface IUserService
{
    /// <summary>
    /// Queries the users.
    /// </summary>
    /// <param name="query">The filter criteria for the users</param>
    /// <param name="paging">The page number and page size</param>
    /// <param name="sort">The sort field and direction, or <see langword="null"/> for the default order</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<PagedList<User>> QueryAsync(
        UserQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a user by id.
    /// </summary>
    /// <param name="userId">The id of the user to return</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<User?> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a locally authenticated user.
    /// </summary>
    /// <param name="create">The values for the new user</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<User?> CreateAsync(
        LocalUserCreate create,
        Guid callerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches a user and enforces the last-administrator and system-account guards.
    /// </summary>
    /// <param name="userId">The id of the user to patch</param>
    /// <param name="patch">The fields to change</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> PatchAsync(
        Guid userId,
        UserPatch patch,
        Guid callerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a user and enforces the last-administrator and system-account guards.
    /// </summary>
    /// <param name="userId">The id of the user to delete</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> DeleteAsync(
        Guid userId,
        Guid callerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unlocks a user and enforces the system-account guard.
    /// </summary>
    /// <param name="userId">The id of the user to unlock</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> UnlockAsync(
        Guid userId,
        Guid callerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the profile of the calling user.
    /// </summary>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<User?> GetProfileAsync(Guid callerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches the profile of the calling user and rejects role and enabled changes.
    /// </summary>
    /// <param name="patch">The profile fields to change</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> UpdateProfileAsync(
        UserPatch patch,
        Guid callerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the password of a user.
    /// </summary>
    /// <param name="userId">The id of the user whose password changes</param>
    /// <param name="change">The new password, with the current one for a self-service change</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="callerIsAdmin">Whether the caller is an administrator</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> ChangePasswordAsync(
        Guid userId,
        PasswordChange change,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a user by id for authentication.
    /// </summary>
    /// <param name="userId">The id of the user to authenticate</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<User?> GetAuthenticationUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a user by username for authentication.
    /// </summary>
    /// <param name="username">The username of the user to authenticate</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<User?> GetAuthenticationUserAsync(
        string username,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the password of a local user.
    /// </summary>
    /// <param name="user">The user that owns the password</param>
    /// <param name="password">The plaintext password to validate</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> VerifyPasswordAsync(
        User user,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the password of a local user.
    /// </summary>
    /// <param name="userId">The id of the user whose password changes</param>
    /// <param name="newPassword">The plaintext password to store</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> SetPasswordAsync(
        Guid userId,
        string newPassword,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);
}
