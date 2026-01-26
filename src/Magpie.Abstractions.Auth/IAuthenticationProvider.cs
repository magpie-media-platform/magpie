using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Result;

using Microsoft.AspNetCore.Http;

namespace Magpie.Abstractions.Auth;

/// <summary>
/// Provides authentication for a specific authentication provider.
/// </summary>
public interface IAuthenticationProvider
{
    /// <summary>
    /// The provider type identifier
    /// </summary>
    string ProviderType { get; }

    /// <summary>
    /// Attempts to authenticate the request.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// A successful result containing the authenticated principal, <see langword="null"/> if this provider
    /// does not apply to the request, or a failure result if authentication failed.
    /// </returns>
    Task<Result<ClaimsPrincipal?>> TryAuthenticateAsync(
        HttpContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds challenge information to the response (e.g., WWW-Authenticate header).
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    void Challenge(HttpContext context);
}
