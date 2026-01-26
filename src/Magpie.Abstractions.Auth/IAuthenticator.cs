using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Result;
using Magpie.Models.Users;

namespace Magpie.Abstractions.Auth;

/// <summary>
/// Validates user credentials for a specific authentication provider.
/// </summary>
public interface IAuthenticator
{
    /// <summary>
    /// The provider type identifier (matches AuthProvider.Provider field).
    /// </summary>
    string ProviderType { get; }

    /// <summary>
    /// Whether this authenticator can validate credentials for users not yet in the database.
    /// </summary>
    /// <remarks>
    /// When enabled, the authenticator may validate credentials for a yet provisioned user, which will be persisted 
    /// to the database on success.
    /// </remarks>
    bool SupportsJitProvisioning { get; }

    /// <summary>
    /// Authenticates the given credentials against this backend.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="password">The password.</param>
    /// <param name="provider">The auth provider configuration with settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// A <see cref="Result{User}"/> containing the authenticated user. 
    /// </returns>
    /// <remarks>
    /// For JIT-capable authenticators, this may be a transient user not yet persisted to the database.
    /// </remarks>
    Task<Result<User>> AuthenticateAsync(
        string username,
        string password,
        AuthProvider provider,
        CancellationToken cancellationToken = default);
}
