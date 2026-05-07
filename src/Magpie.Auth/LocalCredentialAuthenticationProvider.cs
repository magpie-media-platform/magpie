using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Auth;
using Magpie.Models.Data;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Magpie.Auth;

/// <summary>
/// Default credential authentication provider for local username/password accounts.
/// </summary>
/// <remarks>
/// Implements both <see cref="ICredentialAuthenticationProvider"/> for the
/// <c>POST /api/auth/login</c> flow and <see cref="ICredentialManagementAuthenticationProvider"/>
/// so administrators can reset passwords through the same provider.
/// Verification delegates to <see cref="UserManager"/> which owns the password hash format.
/// </remarks>
internal sealed class LocalCredentialAuthenticationProvider(
    UserManager userManager,
    ILogger<LocalCredentialAuthenticationProvider> logger) : ICredentialManagementAuthenticationProvider
{
    /// <inheritdoc />
    public string ProviderType => UserManager.LocalProviderType;

    /// <inheritdoc />
    public async Task<User?> VerifyCredentialsAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        var user = await userManager.GetUserAsync(username, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            logger.LogDebug("Credential authentication failed: user '{Username}' not found", username);
            return null;
        }

        if (!user.Enabled)
        {
            logger.LogDebug("Credential authentication failed: user '{Username}' is disabled", username);
            return null;
        }

        if (!string.Equals(user.ProviderType, ProviderType, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug(
                "Credential authentication skipped: user '{Username}' uses provider '{ProviderType}'",
                username,
                user.ProviderType);
            return null;
        }

        var verified = await userManager.VerifyPasswordAsync(user, password, cancellationToken).ConfigureAwait(false);
        if (!verified)
        {
            logger.LogDebug("Credential authentication failed: bad password for user '{Username}'", username);
            return null;
        }

        return user;
    }

    /// <inheritdoc />
    public Task<ClaimsPrincipal?> TryAuthenticateAsync(
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        // Per-request authentication runs through the bearer provider; this provider only
        // handles the explicit POST /api/auth/login call.
        return Task.FromResult<ClaimsPrincipal?>(null);
    }

    /// <inheritdoc />
    public Task ChallengeAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> ResetPasswordAsync(
        Guid userId,
        string newPassword,
        Guid requestingUserId,
        IReadOnlyDictionary<string, string> verificationPayload,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(newPassword);

        return userManager.SetPasswordAsync(userId, newPassword, requestingUserId, cancellationToken);
    }
}
