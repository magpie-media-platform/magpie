using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Core.Events;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Paging;
using Magpie.Models.Security;
using Magpie.Sdk.Security;
using Magpie.Sdk.Services;

namespace Magpie.Core.Security;

internal sealed class UserService(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IPasswordPolicy passwordPolicy,
    IEventBus eventBus,
    TimeProvider timeProvider) : IUserService
{
    private const string LocalProviderType = "local";

    private static bool IsSystemAccount(User user) =>
        string.Equals(user.Username, "system", StringComparison.Ordinal) &&
        string.Equals(user.ProviderType, "system", StringComparison.Ordinal);

    public async Task<PagedList<User>> QueryAsync(
        UserQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default)
    {
        return await users.QueryAsync(query, paging, sort, cancellationToken).ConfigureAwait(false);
    }

    public async Task<User?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await users.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<User?> CreateAsync(
        LocalUserCreate create,
        Guid callerUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(create.Username))
        {
            throw new MagpieException("Username is required.");
        }

        var username = create.Username.Trim();
        var hashed = await HashPasswordAsync(create.Password, cancellationToken).ConfigureAwait(false);

        if (await FindByUsernameAsync(username, cancellationToken).ConfigureAwait(false) is not null)
        {
            return null;
        }

        return await users
            .CreateAsync(
                new User
                {
                    Username = username,
                    ProviderType = LocalProviderType,
                    Role = create.Role,
                    PasswordHash = hashed.Hash,
                    PasswordSalt = hashed.Salt,
                    Enabled = create.Enabled,
                    CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
                },
                callerUserId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> PatchAsync(
        Guid userId,
        UserPatch patch,
        Guid callerUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null || IsSystemAccount(user))
        {
            return false;
        }

        if (patch.Role is not null || patch.Enabled is not null)
        {
            var resultingRole = patch.Role?.Value ?? user.Role;
            var resultingEnabled = patch.Enabled?.Value ?? user.Enabled;

            var wouldRemoveLastAdmin = await WouldRemoveLastEnabledAdminAsync(
                user,
                resultingRole,
                resultingEnabled,
                cancellationToken)
                .ConfigureAwait(false);
            if (wouldRemoveLastAdmin)
            {
                return false;
            }
        }

        return await users
            .PatchAsync(patch with { Id = userId }, callerUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null || IsSystemAccount(user))
        {
            return false;
        }

        var wouldRemoveLastAdmin = await WouldRemoveLastEnabledAdminAsync(
            user,
            user.Role,
            enabled: false,
            cancellationToken)
            .ConfigureAwait(false);
        if (wouldRemoveLastAdmin)
        {
            return false;
        }

        return await users.DeleteAsync(userId, callerUserId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> UnlockAsync(
        Guid userId,
        Guid callerUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null || IsSystemAccount(user))
        {
            return false;
        }

        if (user.LockedUntil is null && user.FailedLoginAttempts == 0)
        {
            return true;
        }

        var reset = await users.ResetLockoutAsync(userId, callerUserId, cancellationToken).ConfigureAwait(false);
        if (reset)
        {
            await eventBus.PublishAsync(
                SecurityEventBuilder.BuildUserUnlockedEvent(user),
                cancellationToken).ConfigureAwait(false);
        }

        return reset;
    }

    public Task<User?> GetProfileAsync(Guid callerUserId, CancellationToken cancellationToken = default) =>
        GetAsync(callerUserId, cancellationToken);

    public async Task<bool> UpdateProfileAsync(
        UserPatch patch,
        Guid callerUserId,
        CancellationToken cancellationToken = default)
    {
        if (patch.Role is not null || patch.Enabled is not null)
        {
            return false;
        }

        return await users
            .PatchAsync(patch with { Id = callerUserId }, callerUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> ChangePasswordAsync(
        Guid userId,
        PasswordChange change,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default)
    {
        if (!callerIsAdmin && callerUserId != userId)
        {
            return false;
        }

        var user = await users.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return false;
        }

        if (!callerIsAdmin)
        {
            if (string.IsNullOrEmpty(change.CurrentPassword))
            {
                return false;
            }

            var verified = await VerifyPasswordAsync(user, change.CurrentPassword, cancellationToken)
                .ConfigureAwait(false);
            if (!verified)
            {
                return false;
            }
        }

        return await SetPasswordAsync(userId, change.NewPassword, callerUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<User?> GetAuthenticationUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return users.GetByIdAsync(userId, cancellationToken);
    }

    public async Task<User?> GetAuthenticationUserAsync(string username, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        return await FindByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
    }

    private async Task<User?> FindByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        var matches = await users.QueryAsync(
            new UserQuery { Username = username },
            new() { PageNumber = 1, PageSize = 1 },
            null,
            cancellationToken).ConfigureAwait(false);
        return matches.Items.SingleOrDefault();
    }

    public async Task<bool> VerifyPasswordAsync(
        User user,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (string.IsNullOrEmpty(password) ||
            string.IsNullOrEmpty(user.PasswordHash) ||
            string.IsNullOrEmpty(user.PasswordSalt))
        {
            return false;
        }

        return await passwordHasher
            .VerifyPasswordAsync(password, user.PasswordHash, user.PasswordSalt, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> SetPasswordAsync(
        Guid userId,
        string newPassword,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var hashed = await HashPasswordAsync(newPassword, cancellationToken).ConfigureAwait(false);
        return await users
            .SetPasswordAsync(userId, hashed.Hash, hashed.Salt, actingUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<HashedPassword> HashPasswordAsync(
        string password,
        CancellationToken cancellationToken)
    {
        var policyResult = await passwordPolicy
            .ValidatePasswordAsync(password, cancellationToken)
            .ConfigureAwait(false);
        if (!policyResult.IsValid)
        {
            throw new MagpieException(string.Join(" ", policyResult.ValidationErrors));
        }

        return await passwordHasher.HashPasswordAsync(password, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> WouldRemoveLastEnabledAdminAsync(
        User user,
        Models.Enums.Role role,
        bool enabled,
        CancellationToken cancellationToken)
    {
        if (IsSystemAccount(user) ||
            user is not { Role: Models.Enums.Role.Admin, Enabled: true } ||
            (role == Models.Enums.Role.Admin && enabled))
        {
            return false;
        }

        const int pageSize = 500;
        var pageNumber = 1;
        while (true)
        {
            var page = await users.QueryAsync(
                new UserQuery { Role = Models.Enums.Role.Admin, Enabled = true },
                new() { PageNumber = pageNumber, PageSize = pageSize },
                null,
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            if (pageItems.Any(candidate => !IsSystemAccount(candidate) && candidate.Id != user.Id))
            {
                return false;
            }

            if (pageItems.Count < pageSize)
            {
                return true;
            }

            pageNumber++;
        }
    }
}
