using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;

namespace Magpie.Abstractions.Auth;

/// <summary>
/// Provides authentication for a specific authentication provider.
/// </summary>
public interface IAuthenticationProvider
{
    /// <summary>
    /// The unique provider type identifier. 
    /// </summary>
    string ProviderType { get; }

    /// <summary>
    /// Attempts to authenticate the request.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<ClaimsPrincipal?> TryAuthenticateAsync(HttpContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add challenge information to the response.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task ChallengeAsync(HttpContext context, CancellationToken cancellationToken = default);
}
