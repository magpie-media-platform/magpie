using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;

namespace Magpie.Abstractions.Auth;

/// <summary>
/// Provides credential-based authentication for a specific provider type.
/// </summary>
public interface ICredentialAuthenticationProvider : IAuthenticationProvider
{
    /// <summary>
    /// Authenticate a user with credentials.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="password">The password.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The authenticated <see cref="User"/> if successful; otherwise, <see langword="null"/>.</returns>
    Task<User?> VerifyCredentialsAsync(string username, string password, CancellationToken cancellationToken = default);
}
