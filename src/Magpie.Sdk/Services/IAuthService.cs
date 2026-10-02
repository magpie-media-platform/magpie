using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Security;

namespace Magpie.Sdk.Services;

/// <summary>
/// The service contract for authentication, sessions, and effective permissions.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Validates a username and password against the credential providers.
    /// </summary>
    /// <param name="username">The username to authenticate</param>
    /// <param name="password">The plaintext password to validate</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<User?> VerifyCredentialsAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a username and password and creates a session.
    /// </summary>
    /// <param name="username">The username to authenticate</param>
    /// <param name="password">The plaintext password to validate</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<AuthSession?> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a session from a refresh token and rotates that token.
    /// </summary>
    /// <param name="refreshToken">The refresh token issued with the previous session</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<AuthSession?> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes a session with a renewal key.
    /// </summary>
    /// <param name="refreshToken">The previous or current refresh token</param>
    /// <param name="renewalKey">The client proof retained until renewal completes</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<AuthSession?> RefreshAsync(
        string refreshToken, string renewalKey, CancellationToken cancellationToken = default)
        => RefreshAsync(refreshToken, cancellationToken);

    /// <summary>
    /// Revokes a refresh token.
    /// </summary>
    /// <param name="refreshToken">The refresh token to revoke</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> LogoutAsync(
        string refreshToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a session, including a renewal whose response was interrupted.
    /// </summary>
    /// <param name="refreshToken">The previous or current refresh token</param>
    /// <param name="renewalKey">The client proof for a pending renewal</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> LogoutAsync(string refreshToken, string renewalKey, CancellationToken cancellationToken = default)
        => LogoutAsync(refreshToken, cancellationToken);

    /// <summary>
    /// Gets the grant state of every named permission for a role.
    /// </summary>
    /// <param name="role">The role to resolve the permissions of</param>
    EffectivePermissions GetEffectivePermissions(Role role);

    /// <summary>
    /// Lists the registered credential provider types.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<string>> ListCredentialProviderTypesAsync(CancellationToken cancellationToken = default);
}
