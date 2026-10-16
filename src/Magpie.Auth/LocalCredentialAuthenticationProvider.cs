using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models.Data;
using Magpie.Sdk.Auth;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Magpie.Auth;

internal sealed class LocalCredentialAuthenticationProvider(
    IUserService userService,
    IUserRepository userRepository,
    AccountLockoutOptions lockoutOptions,
    TimeProvider timeProvider,
    ILogger<LocalCredentialAuthenticationProvider> logger) : ICredentialAuthenticationProvider
{
    private static readonly User DummyVerificationUser = new()
    {
        Username = "\0",
        ProviderType = CredentialProviderTypes.Local,
        PasswordHash = "i=600000$" + Convert.ToBase64String(new byte[32]),
        PasswordSalt = Convert.ToBase64String(new byte[32]),
    };

    public string ProviderType => CredentialProviderTypes.Local;

    public Task<AuthenticationAttempt> TryAuthenticateAsync(
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(AuthenticationAttempt.None);
    }

    public Task ChallengeAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public async Task<User?> VerifyCredentialsAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        var user = await userService.GetAuthenticationUserAsync(username, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            logger.LogDebug("User {Username} does not exist", username);
            await RunDummyVerificationAsync(password, cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (!user.Enabled)
        {
            logger.LogDebug("User {Username} is disabled", username);
            await RunDummyVerificationAsync(password, cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (!string.Equals(user.ProviderType, ProviderType, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug(
                "User {Username} uses provider {ProviderType}, skipped",
                username,
                user.ProviderType);
            await RunDummyVerificationAsync(password, cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (lockoutOptions.Enabled && user.LockedUntil is { } lockedUntil)
        {
            if (lockedUntil > timeProvider.GetUtcNow().UtcDateTime)
            {
                logger.LogWarning(
                    "User {Username} is locked until {LockedUntil}",
                    username,
                    lockedUntil);
                return null;
            }

            await userRepository.ResetLockoutAsync(user.Id, cancellationToken: cancellationToken).ConfigureAwait(false);

            user.FailedLoginAttempts = 0;
            user.LockedUntil = null;
        }

        var verified = await userService.VerifyPasswordAsync(user, password, cancellationToken).ConfigureAwait(false);
        if (!verified)
        {
            await RecordFailureAsync(user, username, cancellationToken).ConfigureAwait(false);
            return null;
        }

        await userRepository.ClearFailedLoginsAsync(user.Id, cancellationToken).ConfigureAwait(false);
        return user;
    }

    private Task RunDummyVerificationAsync(string password, CancellationToken cancellationToken)
    {
        return userService.VerifyPasswordAsync(DummyVerificationUser, password, cancellationToken);
    }

    private async Task RecordFailureAsync(User user, string username, CancellationToken cancellationToken)
    {
        if (!lockoutOptions.Enabled)
        {
            logger.LogDebug("User {Username} gave a bad password", username);
            return;
        }

        var result = await userRepository
            .RecordFailedLoginAsync(
                user.Id,
                lockoutOptions.MaxFailedAttempts,
                timeProvider.GetUtcNow().UtcDateTime + lockoutOptions.LockoutDuration,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.LockedUntil is not null)
        {
            Telemetry.Lockouts.Add(1);
            logger.LogInformation(
                "Locked user {Username} until {LockedUntil} after {Attempts} failed attempts",
                username,
                result.LockedUntil,
                result.FailedAttempts);
        }
        else
        {
            logger.LogDebug(
                "User {Username} gave a bad password, {Attempts} of {Max} failed attempts",
                username,
                result.FailedAttempts,
                lockoutOptions.MaxFailedAttempts);
        }
    }
}
