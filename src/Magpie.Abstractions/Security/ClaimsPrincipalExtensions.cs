using System;
using System.Security.Claims;

namespace Magpie.Abstractions.Security;

/// <summary>
/// Extension methods for <see cref="ClaimsPrincipal"/>.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Get the user id from the claims principal.
    /// </summary>
    /// <param name="principal">The claims principal.</param>
    /// <returns>The user id if present and valid; otherwise, <see langword="null"/>.</returns>
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim is null || !Guid.TryParse(userIdClaim.Value, out var userId))
        {
            return null;
        }

        return userId;
    }
}
