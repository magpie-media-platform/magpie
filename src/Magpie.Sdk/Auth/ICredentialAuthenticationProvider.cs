using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;

namespace Magpie.Sdk.Auth;

/// <summary>
/// An authentication provider that verifies credentials.
/// </summary>
public interface ICredentialAuthenticationProvider : IAuthenticationProvider
{
    /// <summary>
    /// Verifies a username and password.
    /// </summary>
    /// <param name="username">The username</param>
    /// <param name="password">The password</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The authenticated user, or <see langword="null"/> when verification fails</returns>
    Task<User?> VerifyCredentialsAsync(string username, string password, CancellationToken cancellationToken = default);
}
