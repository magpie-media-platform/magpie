using System;
using System.Security.Claims;

namespace Magpie.Abstractions.Auth;

/// <summary>
/// Extension methods for <see cref="ClaimsIdentity"/> to extract standard claim values.
/// </summary>
public static class ClaimsIdentityExtensions
{
    /// <summary>
    /// Get the Magpie user ID from the principal.
    /// </summary>
    /// <param name="principal">The claims principal.</param>
    /// <returns>The user ID, or null if not present.</returns>
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        var claim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return claim is not null && Guid.TryParse(claim, out var userId) ? userId : null;
    }

    /// <summary>
    /// Get the username from the identity (Name or Upn claim).
    /// </summary>
    /// <param name="identity">The claims identity.</param>
    /// <returns>The username, or null if not present.</returns>
    public static string? GetUsername(this ClaimsIdentity identity)
    {
        return identity.FindFirst(ClaimTypes.Name)?.Value ?? identity.FindFirst(ClaimTypes.Upn)?.Value;
    }

    /// <summary>
    /// Get the email address from the identity (Email claim).
    /// </summary>
    /// <param name="identity">The claims identity.</param>
    /// <returns>The email address, or null if not present.</returns>
    public static string? GetEmail(this ClaimsIdentity identity)
    {
        return identity.FindFirst(ClaimTypes.Email)?.Value;
    }
}
