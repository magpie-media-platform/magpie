using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Auth;
using Magpie.Abstractions.Data;
using Magpie.Abstractions.Security;
using Magpie.Core;
using Magpie.Models.Result;
using Magpie.Models.Users;

namespace Magpie.Auth;

internal sealed class LocalAuthenticator(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher) : IAuthenticator
{
    public const string ProviderTypeName = "local";

    public static readonly Guid ProviderId = new("22222222-2222-2222-2222-222222222222");

    public string ProviderType => ProviderTypeName;

    public bool SupportsJitProvisioning => false;

    public Task<Result<User>> AuthenticateAsync(
        string username,
        string password,
        AuthProvider provider,
        CancellationToken cancellationToken = default)
    {
        return userRepository.GetUserCredentialsAsync(username, cancellationToken)
            .BindAsync(credentials => credentials switch
            {
                null =>
                    Result.Fail<UserCredentials>("invalid credentials", ErrorCodes.InvalidCredentials),
                { LockedUntil: { } until } when until > DateTimeOffset.UtcNow =>
                    Result.Fail<UserCredentials>("account locked out", ErrorCodes.AccountLockedOut),
                { Enabled: false } =>
                    Result.Fail<UserCredentials>("invalid credentials", ErrorCodes.AccountDisabled),
                { PasswordHash: null or "" } or { PasswordSalt: null or "" } =>
                    Result.Fail<UserCredentials>("invalid credentials", ErrorCodes.InvalidCredentials),
                _ =>
                    Result.Success(credentials)
            })
            .BindAsync(credentials => VerifyPasswordAsync(credentials, password, cancellationToken))
            .BindAsync(credentials => GetAuthenticatedUserAsync(credentials.UserId, cancellationToken));
    }

    private Task<Result<UserCredentials>> VerifyPasswordAsync(
        UserCredentials credentials,
        string password,
        CancellationToken cancellationToken)
    {
        return passwordHasher
            .VerifyPasswordAsync(password, credentials.PasswordHash!, credentials.PasswordSalt!, cancellationToken)
            .BindAsync(verified => verified
                ? userRepository.ResetFailedLoginAttemptsAsync(credentials.UserId, cancellationToken)
                    .MapAsync(_ => credentials)
                : userRepository.RecordFailedLoginAsync(credentials.UserId, null, cancellationToken)
                    .BindAsync(_ => Result.Fail<UserCredentials>(
                        "Invalid username or password",
                        ErrorCodes.InvalidCredentials)));
    }

    private async Task<Result<User>> GetAuthenticatedUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var userResult = await userRepository.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);

        return userResult.Bind(user => user is not null
            ? Result.Success(user)
            : Result.Fail<User>("Invalid username or password", ErrorCodes.InvalidCredentials));
    }
}
