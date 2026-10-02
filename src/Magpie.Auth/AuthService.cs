using System;
using System.Collections.Generic;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Auth;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Models;
using Magpie.Models.Authorization;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Security;
using Magpie.Sdk.Auth;
using Magpie.Sdk.Events;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Magpie.Auth;

internal sealed class AuthService(
    IEnumerable<ICredentialAuthenticationProvider> credentialProviders,
    ITokenService tokenService,
    IUserService userService,
    IEventBus eventBus,
    AccountLockoutOptions lockoutOptions,
    TimeProvider timeProvider,
    ILogger<AuthService> logger,
    ITransactionManager transactionManager,
    IRefreshTokenRepository refreshTokenRepository,
    IDataProtectionService protector) : IAuthService
{
    private readonly IReadOnlyList<ICredentialAuthenticationProvider> _credentialProviders =
    [
        .. credentialProviders.OrderByDescending(p =>
            string.Equals(
                p.ProviderType,
                CredentialProviderTypes.Local,
                StringComparison.OrdinalIgnoreCase))
    ];

    private readonly IReadOnlyDictionary<string, Role> _requiredRoles = new Dictionary<string, Role>
    {
        [Permissions.ContentView] = Role.Viewer,
        [Permissions.ContentEdit] = Role.Manager,
        [Permissions.ContentAdd] = Role.Requester,
        [Permissions.ContentDelete] = Role.Manager,
        [Permissions.ContentRequest] = Role.Requester,
        [Permissions.SubjectsView] = Role.Viewer,
        [Permissions.SubjectsEdit] = Role.Requester,
        [Permissions.DownloadsView] = Role.Viewer,
        [Permissions.DownloadsControl] = Role.Requester,
        [Permissions.DownloadsManage] = Role.Manager,
        [Permissions.IndexersManage] = Role.Admin,
        [Permissions.ClientsManage] = Role.Admin,
        [Permissions.ServersManage] = Role.Admin,
        [Permissions.FoldersManage] = Role.Admin,
        [Permissions.JobsView] = Role.Viewer,
        [Permissions.JobsRun] = Role.Requester,
        [Permissions.JobsSchedule] = Role.Requester,
        [Permissions.JobsManage] = Role.Manager,
        [Permissions.WishlistsView] = Role.Viewer,
        [Permissions.WishlistsOwn] = Role.Requester,
        [Permissions.WishlistsManage] = Role.Requester,
        [Permissions.QualityView] = Role.Viewer,
        [Permissions.QualityManage] = Role.Admin,
        [Permissions.TagsView] = Role.Viewer,
        [Permissions.TagsManage] = Role.Manager,
        [Permissions.FiltersView] = Role.Viewer,
        [Permissions.FiltersOwn] = Role.Viewer,
        [Permissions.FiltersManage] = Role.Viewer,
        [Permissions.ActivityView] = Role.Viewer,
        [Permissions.CalendarView] = Role.Viewer,
        [Permissions.SystemView] = Role.Viewer,
        [Permissions.SettingsView] = Role.Manager,
        [Permissions.SettingsEdit] = Role.Admin,
        [Permissions.UsersManage] = Role.Admin,
        [Permissions.ExtensionsManage] = Role.Admin,
        [Permissions.BackupsManage] = Role.Admin,
        [Permissions.FilesBrowse] = Role.Manager,
    }.AsReadOnly();

    private static string Hash(string value)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool HasAccessTokenId(string accessToken, string? identifier)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            return handler.CanReadToken(accessToken) && handler.ReadJwtToken(accessToken).Id == identifier;
        }
        catch (Exception ex) when (ex is ArgumentException or SecurityTokenException)
        {
            return false;
        }
    }

    private static MagpieEvent BuildUserLockedOut(
        Guid userId,
        string username,
        int failedAttemptCount,
        DateTime lockedUntil)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.UserId] = userId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.UserUsername] = username ?? string.Empty,
            [MagpieEvents.Keys.UserFailedAttemptCount] = failedAttemptCount.ToString(CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.UserLockedUntil] = lockedUntil.ToString("o", CultureInfo.InvariantCulture),
        };

        return new MagpieEvent(Guid.Empty, MagpieEvents.Names.UserLockedOut, context, default, default, null);
    }

    public async Task<User?> VerifyCredentialsAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        foreach (var provider in _credentialProviders)
        {
            var user = await provider
                .VerifyCredentialsAsync(username, password, cancellationToken)
                .ConfigureAwait(false);

            if (user is not null)
            {
                return user;
            }
        }

        logger.LogDebug(
            "No credential provider accepted the credentials for user {Username}",
            username);
        return null;
    }

    public async Task<AuthSession?> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var wasLocked = false;
        if (lockoutOptions.Enabled)
        {
            var existing = await userService.GetAuthenticationUserAsync(username, cancellationToken)
                .ConfigureAwait(false);
            wasLocked = existing?.LockedUntil is { } locked && locked > timeProvider.GetUtcNow().UtcDateTime;
        }

        var user = await VerifyCredentialsAsync(username, password, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            if (lockoutOptions.Enabled && !wasLocked)
            {
                var current = await userService.GetAuthenticationUserAsync(username, cancellationToken)
                    .ConfigureAwait(false);
                if (current?.LockedUntil is { } lockedUntil && lockedUntil > timeProvider.GetUtcNow().UtcDateTime)
                {
                    await eventBus
                        .PublishAsync(
                            BuildUserLockedOut(
                                current.Id,
                                current.Username,
                                current.FailedLoginAttempts,
                                lockedUntil),
                            cancellationToken)
                        .ConfigureAwait(false);
                    Telemetry.Lockouts.Add(1);
                }
            }

            Telemetry.LoginAttempts.Add(1, new KeyValuePair<string, object?>("outcome", "failure"));
            return null;
        }

        var accessToken = await tokenService.GenerateAccessTokenAsync(user, cancellationToken).ConfigureAwait(false);
        var refreshToken = await tokenService
            .GenerateRefreshTokenAsync(user.Id, accessToken.Jti, cancellationToken)
            .ConfigureAwait(false);

        Telemetry.LoginAttempts.Add(1, new KeyValuePair<string, object?>("outcome", "success"));

        return new AuthSession
        {
            User = user,
            AccessToken = accessToken.Token,
            AccessTokenExpiresAt = accessToken.ExpiresAt,
            RefreshToken = refreshToken.Token,
            RefreshTokenExpiresAt = refreshToken.ExpiresAt,
        };
    }

    public Task<AuthSession?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        return transactionManager.ExecuteWithTransactionAsync(
            token => RefreshCoreAsync(refreshToken, token),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<AuthSession?> RefreshAsync(
        string refreshToken,
        string renewalKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(renewalKey);
        return transactionManager.ExecuteWithTransactionAsync(
            async token =>
            {
                var recovered = await RecoverRenewalAsync(refreshToken, renewalKey, token).ConfigureAwait(false);
                if (recovered is not null)
                {
                    return recovered;
                }

                var previous = await refreshTokenRepository.GetByHashAsync(Hash(refreshToken), token)
                    .ConfigureAwait(false);
                var session = await RefreshCoreAsync(refreshToken, token).ConfigureAwait(false);
                if (session is null)
                {
                    // A concurrent request can commit the same rotation while this request waits for its write lock.
                    return await RecoverRenewalAsync(refreshToken, renewalKey, token).ConfigureAwait(false);
                }

                if (previous is null)
                {
                    throw new InvalidOperationException("The consumed refresh token is missing.");
                }

                var current = await refreshTokenRepository.GetByHashAsync(Hash(session.RefreshToken), token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The rotated refresh token is missing.");
                await refreshTokenRepository
                    .StoreRenewalAsync(
                        current with
                        {
                            PreviousTokenHash = previous.TokenHash,
                            RenewalKeyHash = Hash(renewalKey),
                            RenewalExpiresAt = previous.ExpiresAt,
                            ProtectedRenewal = protector.Protect(JsonSerializer.Serialize(new RenewalTokens(
                                session.AccessToken,
                                session.AccessTokenExpiresAt,
                                session.RefreshToken,
                                session.RefreshTokenExpiresAt)))
                        },
                        token)
                    .ConfigureAwait(false);
                return session;
            },
            cancellationToken);
    }

    private async Task<AuthSession?> RecoverRenewalAsync(
        string refreshToken,
        string renewalKey,
        CancellationToken cancellationToken)
    {
        var receipt = await refreshTokenRepository
            .GetRenewalAsync(Hash(refreshToken), Hash(renewalKey), cancellationToken)
            .ConfigureAwait(false);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (receipt?.ProtectedRenewal is null || receipt.ExpiresAt <= now ||
            receipt.RenewalExpiresAt is not { } renewalExpiry || renewalExpiry <= now)
        {
            return null;
        }

        var user = await userService.GetAuthenticationUserAsync(receipt.UserId, cancellationToken)
            .ConfigureAwait(false);
        if (user is null || !user.Enabled || user.LockedUntil > now)
        {
            return null;
        }

        var tokens = JsonSerializer.Deserialize<RenewalTokens>(protector.Unprotect(receipt.ProtectedRenewal))
            ?? throw new InvalidOperationException("The renewal receipt is invalid.");
        return new AuthSession
        {
            User = user,
            AccessToken = tokens.AccessToken,
            AccessTokenExpiresAt = tokens.AccessTokenExpiresAt,
            RefreshToken = tokens.RefreshToken,
            RefreshTokenExpiresAt = tokens.RefreshTokenExpiresAt
        };
    }

    private async Task<AuthSession?> RefreshCoreAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var userId = await tokenService
            .TryConsumeRefreshTokenAsync(refreshToken, cancellationToken)
            .ConfigureAwait(false);

        if (userId is null)
        {
            Telemetry.TokensRefreshed.Add(1, new KeyValuePair<string, object?>("outcome", "invalid_or_expired"));
            return null;
        }

        var user = await userService.GetAuthenticationUserAsync(userId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (user is null || !user.Enabled || user.LockedUntil > timeProvider.GetUtcNow().UtcDateTime)
        {
            Telemetry.TokensRefreshed.Add(1, new KeyValuePair<string, object?>("outcome", "invalid_or_expired"));
            return null;
        }

        var accessToken = await tokenService.GenerateAccessTokenAsync(user, cancellationToken).ConfigureAwait(false);
        var rotatedToken = await tokenService
            .GenerateRefreshTokenAsync(user.Id, accessToken.Jti, cancellationToken)
            .ConfigureAwait(false);

        Telemetry.TokensRefreshed.Add(1, new KeyValuePair<string, object?>("outcome", "success"));

        return new AuthSession
        {
            User = user,
            AccessToken = accessToken.Token,
            AccessTokenExpiresAt = accessToken.ExpiresAt,
            RefreshToken = rotatedToken.Token,
            RefreshTokenExpiresAt = rotatedToken.ExpiresAt,
        };
    }

    public async Task<bool> LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        await tokenService.RevokeRefreshTokenAsync(refreshToken, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public Task<bool> LogoutAsync(string refreshToken, string renewalKey, CancellationToken cancellationToken = default)
    {
        return transactionManager.ExecuteWithTransactionAsync(
            async token =>
            {
                var receipt = await refreshTokenRepository.GetRenewalAsync(Hash(refreshToken), Hash(renewalKey), token)
                    .ConfigureAwait(false);
                if (receipt is not null)
                {
                    await refreshTokenRepository.DeleteAsync(receipt.TokenHash, token).ConfigureAwait(false);
                }
                else
                {
                    var current = await refreshTokenRepository.GetByHashAsync(Hash(refreshToken), token)
                        .ConfigureAwait(false);
                    if (current is not null && !HasAccessTokenId(renewalKey, current.AccessTokenJti))
                    {
                        return false;
                    }
                }

                return await LogoutAsync(refreshToken, token).ConfigureAwait(false);
            },
            cancellationToken);
    }

    public EffectivePermissions GetEffectivePermissions(Role role)
    {
        var permissions = new Dictionary<string, PermissionGrant>();
        foreach (var (permission, requiredRole) in _requiredRoles)
        {
            permissions[permission] = new PermissionGrant
            {
                RequiredRole = requiredRole,
                Granted = role >= requiredRole,
            };
        }

        return new EffectivePermissions { Role = role, Permissions = permissions };
    }

    public Task<IReadOnlyList<string>> ListCredentialProviderTypesAsync(CancellationToken cancellationToken = default)
    {
        var types = credentialProviders
            .Select(provider => provider.ProviderType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult<IReadOnlyList<string>>(types);
    }

    private sealed record RenewalTokens(
        string AccessToken,
        DateTime AccessTokenExpiresAt,
        string RefreshToken,
        DateTime RefreshTokenExpiresAt);
}
