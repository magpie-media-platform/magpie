using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Auth;
using Magpie.Abstractions.Data;
using Magpie.Abstractions.Security;
using Magpie.Models.Result;
using Magpie.Models.Users;

using static Magpie.Abstractions.Security.IPasswordHasher;

namespace Magpie.Core.Security;

/// <inheritdoc />
public sealed class UserManager(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IPasswordPolicy passwordPolicy) : IUserManager
{
    /// <inheritdoc />
    public Task<Result<IEnumerable<User>>> GetUsersAsync(
        Guid? authProviderId = null,
        Role? role = null,
        bool? enabled = null,
        CancellationToken cancellationToken = default)
    {
        return userRepository.GetAllAsync(cancellationToken)
            .MapAsync(users => role is not null
                ? users.Where(u => u.Role == role)
                : users)
            .MapAsync(users => enabled is not null
                ? users.Where(u => u.Enabled == enabled)
                : users)
            .MapAsync(users => authProviderId is not null
                ? users.Where(u => u.AuthProviderId == authProviderId)
                : users);
    }

    /// <inheritdoc />
    public Task<Result<User?>> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return userRepository.GetByIdAsync(userId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<User?>> GetUserByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        return userRepository.GetByUsernameAsync(username, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<User?>> GetUserByExternalIdAsync(
        Guid authProviderId,
        string externalId,
        CancellationToken cancellationToken = default)
    {
        return userRepository.GetByExternalIdAsync(authProviderId, externalId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<User>> CreateLocalUserAsync(
        string username,
        string? email,
        string password,
        Role role,
        bool enabled,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        return passwordPolicy
            .ValidatePasswordAsync(password, cancellationToken)
            .BindAsync(async validation => validation switch
            {
                { IsValid: true } =>
                    await userRepository.GetByUsernameAsync(username, cancellationToken).ConfigureAwait(false),
                { IsValid: false, ValidationErrors: { Count: > 0 } errors } =>
                    Result.Fail<User?>(string.Join(",", errors), ErrorCodes.InvalidCredentials),
                { IsValid: false } =>
                    Result.Fail<User?>("invalid password", ErrorCodes.InvalidCredentials),
            })
            .BindAsync(existingUser => existingUser is not null
                ? Result.Fail<Unit>("Username already exists", ErrorCodes.UsernameAlreadyExists)
                : Result.Success(Unit.Default))
            .BindAsync(_ => passwordHasher.HashPasswordAsync(password, cancellationToken))
            .BindAsync(hashedPassword => userRepository.CreateAsync(
                username,
                email ?? string.Empty,
                Guid.Empty,
                null,
                enabled,
                role,
                hashedPassword.Hash,
                hashedPassword.Salt,
                ImmutableDictionary<string, string>.Empty,
                DateTimeOffset.UtcNow,
                createdByUserId,
                cancellationToken));
    }

    /// <inheritdoc />
    public Task<Result<User>> CreateExternalUserAsync(
        string username,
        string? email,
        Guid authProviderId,
        string? externalId,
        Role role,
        bool enabled,
        IReadOnlyDictionary<string, string> providerData,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        return userRepository.GetByUsernameAsync(username, cancellationToken)
            .BindAsync(existingUser => existingUser is not null
                ? Result.Fail<Unit>("Username already exists", ErrorCodes.UsernameAlreadyExists)
                : Result.Success(Unit.Default))
            .BindAsync(_ => userRepository.CreateAsync(
                username,
                email ?? string.Empty,
                authProviderId,
                externalId,
                enabled,
                role,
                null,
                null,
                providerData,
                DateTimeOffset.UtcNow,
                createdByUserId,
                cancellationToken));
    }

    /// <inheritdoc />
    public Task<Result<User>> UpdateUserAsync(
        UserPatch userPatch,
        Guid modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        return userRepository.PatchAsync(userPatch, modifiedByUserId, cancellationToken)
            .BindAsync(async patched => patched
                ? await userRepository.GetByIdAsync(userPatch.Id, cancellationToken).ConfigureAwait(false)
                : Result.Fail<User?>("User not found", ErrorCodes.UserNotFound))
            .BindAsync(user => user is not null
                ? Result.Success(user)
                : Result.Fail<User>("User not found", ErrorCodes.UserNotFound));
    }

    /// <inheritdoc />
    public async Task<Result<bool>> DeleteUserAsync(
        Guid userId,
        Guid? deletedByUserId,
        CancellationToken cancellationToken)
    {
        return await userRepository.DeleteAsync(userId, deletedByUserId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result<User?>> VerifyCredentialsAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        return userRepository.GetUserCredentialsAsync(username, cancellationToken)
            .BindAsync(credentials => credentials switch
            {
                { LockedUntil: { } until } when until > DateTimeOffset.UtcNow => Result.Success<UserCredentials?>(null),
                { PasswordHash: null or "" } or { PasswordSalt: null or "" } => Result.Success<UserCredentials?>(null),
                { Enabled: false } or null => Result.Success<UserCredentials?>(null),
                _ => Result.Success<UserCredentials?>(credentials)
            })
            .BindAsync(credentials => VerifyPasswordAsync(credentials, password, cancellationToken))
            .BindAsync(credentials => GetVerifiedUserAsync(credentials, cancellationToken));
    }

    private async Task<Result<UserCredentials?>> VerifyPasswordAsync(
        UserCredentials? credentials,
        string password,
        CancellationToken cancellationToken)
    {
        if (credentials is null)
        {
            return Result.Success<UserCredentials?>(null);
        }

        var verifyResult = await passwordHasher
            .VerifyPasswordAsync(password, credentials.PasswordHash!, credentials.PasswordSalt!, cancellationToken)
            .ConfigureAwait(false);

        if (verifyResult is not SuccessResult<bool> { Value: true })
        {
            await userRepository.RecordFailedLoginAsync(credentials.UserId, null, cancellationToken)
                .ConfigureAwait(false);

            return Result.Success<UserCredentials?>(null);
        }

        await userRepository.ResetFailedLoginAttemptsAsync(credentials.UserId, cancellationToken).ConfigureAwait(false);

        return Result.Success<UserCredentials?>(credentials);
    }

    private async Task<Result<User?>> GetVerifiedUserAsync(
        UserCredentials? credentials,
        CancellationToken cancellationToken)
    {
        return credentials is null
            ? Result.Success<User?>(null)
            : await userRepository.GetByIdAsync(credentials.UserId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result<Unit>> ResetPasswordAsync(
        Guid userId,
        string password,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        return passwordPolicy
            .ValidatePasswordAsync(password, cancellationToken)
            .BindAsync(validation => validation switch
            {
                { IsValid: true } =>
                    passwordHasher.HashPasswordAsync(password, cancellationToken),
                { IsValid: false, ValidationErrors: { Count: > 0 } errors } =>
                    Result.FailAsync<HashedPassword>(string.Join(", ", errors), ErrorCodes.InvalidCredentials),
                { IsValid: false } =>
                    Result.FailAsync<HashedPassword>("invalid password", ErrorCodes.InvalidCredentials),
            })
            .BindAsync(hashedPassword => userRepository.UpdatePasswordAsync(
                userId,
                hashedPassword.Hash,
                hashedPassword.Salt,
                resetFailedLoginAttempts: true,
                unlock: true,
                modifiedByUserId: modifiedByUserId,
                cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public Task<Result<ClaimsPrincipal>> CreatePrincipalAsync(User user, CancellationToken cancellationToken = default)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role.ToString())
        };

        if (user.AuthProviderId.HasValue)
        {
            claims.Add(new Claim(MagpieClaims.AuthProviderId, user.AuthProviderId.Value.ToString()));
        }

        var identity = new ClaimsIdentity(claims, "Magpie");
        var principal = new ClaimsPrincipal(identity);

        return Result.SuccessAsync(principal);
    }
}
