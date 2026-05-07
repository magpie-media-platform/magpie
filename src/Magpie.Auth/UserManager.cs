using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Core.Security;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Paging;

namespace Magpie.Auth;

/// <summary>
/// Default <see cref="IUserManager"/> implementation that wraps <see cref="IUserRepository"/>
/// and uses <see cref="IPasswordHasher"/> for password storage on local accounts.
/// </summary>
internal sealed class UserManager(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher) : IUserManager
{
    /// <summary>
    /// The provider type used for local username/password accounts.
    /// </summary>
    public const string LocalProviderType = "local";

    /// <inheritdoc />
    public Task<PagedList<User>> QueryAsync(
        UserQuery query,
        SortParameter? sort = null,
        PagingParameter? paging = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return userRepository.QueryAsync(query, paging, sort, cancellationToken);
    }

    /// <inheritdoc />
    public Task<User?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return userRepository.GetByIdAsync(userId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<User?> GetUserAsync(string username, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        return userRepository.GetUserAsync(username, cancellationToken);
    }

    /// <inheritdoc />
    public Task<User?> GetUserAsync(
        string providerType,
        string externalId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerType);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);

        return userRepository.GetUserAsync(providerType, externalId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<User> CreateUserAsync(User user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        return userRepository.CreateAsync(user, user.CreatedByUserId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<User> CreateLocalUserAsync(UserCreate createArgs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(createArgs);

        var user = new User
        {
            Username = createArgs.Username,
            ProviderType = LocalProviderType,
            Role = createArgs.Role,
            PasswordHash = createArgs.PasswordHash,
            PasswordSalt = createArgs.PasswordSalt,
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
        };

        return userRepository.CreateAsync(user, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<User> UpdateUserAsync(
        UserPatch userPatch,
        Guid modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userPatch);

        var patched = await userRepository.PatchAsync(userPatch, modifiedByUserId, cancellationToken)
            .ConfigureAwait(false);

        if (!patched)
        {
            throw new InvalidOperationException("User not found");
        }

        var user = await userRepository.GetByIdAsync(userPatch.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("User not found after update");

        return user;
    }

    /// <inheritdoc />
    public Task<bool> DeleteUserAsync(
        Guid userId,
        Guid? deletedByUserId,
        CancellationToken cancellationToken = default)
    {
        return userRepository.DeleteAsync(userId, deletedByUserId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ClaimsPrincipal> CreatePrincipalAsync(User user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        return Task.FromResult(user.ToPrincipal());
    }

    public async Task<bool> VerifyPasswordAsync(
        User user,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        if (string.IsNullOrEmpty(user.PasswordHash) || string.IsNullOrEmpty(user.PasswordSalt))
        {
            return false;
        }

        return await passwordHasher.VerifyPasswordAsync(
                password,
                user.PasswordHash,
                user.PasswordSalt,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> SetPasswordAsync(
        Guid userId,
        string newPassword,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(newPassword);

        var user = await userRepository.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return false;
        }

        var hashed = await passwordHasher.HashPasswordAsync(newPassword, cancellationToken).ConfigureAwait(false);

        user.PasswordHash = hashed.Hash;
        user.PasswordSalt = hashed.Salt;

        return await userRepository.UpdateAsync(user, modifiedByUserId, cancellationToken).ConfigureAwait(false);
    }
}
