using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;

namespace Magpie.Sdk.Auth;

/// <summary>
/// An authentication provider.
/// </summary>
public interface IAuthenticationProvider
{
    /// <summary>
    /// The provider type.
    /// </summary>
    string ProviderType { get; }

    /// <summary>
    /// Attempts to authenticate the request.
    /// </summary>
    /// <param name="context">The HTTP context</param>
    /// <param name="cancellationToken">The cancellation token</param>
    Task<AuthenticationAttempt> TryAuthenticateAsync(
        HttpContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds challenge information to the response.
    /// </summary>
    /// <param name="context">The HTTP context</param>
    /// <param name="cancellationToken">The cancellation token</param>
    Task ChallengeAsync(HttpContext context, CancellationToken cancellationToken = default);
}
