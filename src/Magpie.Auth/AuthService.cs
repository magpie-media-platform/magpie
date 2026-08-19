using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Auth;
using Magpie.Models;
using Magpie.Models.Authorization;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Security;
using Magpie.Sdk.Auth;
using Magpie.Sdk.Events;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Auth;

internal sealed class AuthService(
    IEnumerable<ICredentialAuthenticationProvider> credentialProviders,
    ITokenService tokenService,
    IUserService userService,
    IEventBus eventBus,
    AccountLockoutOptions lockoutOptions,
    TimeProvider timeProvider,
    ILogger<AuthService> logger) : IAuthService
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
        [Permissions.ContentAdd] = Role.Manager,
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
        [Permissions.JobsView] = Role.Admin,
        [Permissions.JobsRun] = Role.Manager,
        [Permissions.JobsSchedule] = Role.Requester,
        [Permissions.JobsManage] = Role.Admin,
        [Permissions.WishlistsView] = Role.Viewer,
        [Permissions.WishlistsOwn] = Role.Requester,
        [Permissions.WishlistsManage] = Role.Manager,
        [Permissions.QualityView] = Role.Admin,
        [Permissions.QualityManage] = Role.Admin,
        [Permissions.TagsView] = Role.Viewer,
        [Permissions.TagsManage] = Role.Manager,
        [Permissions.FiltersView] = Role.Viewer,
        [Permissions.FiltersOwn] = Role.Requester,
        [Permissions.FiltersManage] = Role.Manager,
        [Permissions.ActivityView] = Role.Viewer,
        [Permissions.CalendarView] = Role.Viewer,
        [Permissions.SystemView] = Role.Admin,
        [Permissions.SettingsView] = Role.Admin,
        [Permissions.SettingsEdit] = Role.Admin,
        [Permissions.UsersManage] = Role.Admin,
        [Permissions.ExtensionsManage] = Role.Admin,
        [Permissions.BackupsManage] = Role.Admin,
        [Permissions.FilesBrowse] = Role.Admin,
    }.AsReadOnly();

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
            "Credential authentication failed for user {Username}. No provider accepted the credentials",
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

    public async Task<AuthSession?> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
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
        if (user is null || !user.Enabled)
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

    public async Task<bool> LogoutAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        await tokenService.RevokeRefreshTokenAsync(refreshToken, cancellationToken).ConfigureAwait(false);
        return true;
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

        return new EffectivePermissions
        {
            Role = role,
            Permissions = permissions,
        };
    }

    public Task<IReadOnlyList<string>> ListCredentialProviderTypesAsync(CancellationToken cancellationToken = default)
    {
        var types = credentialProviders
            .Select(provider => provider.ProviderType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult<IReadOnlyList<string>>(types);
    }
}
