
using System.Collections.Generic;
using System.Security.Claims;

using Magpie.Abstractions.Auth;
using Magpie.Models.Data;

namespace Magpie.Core.Security;

/// <summary>
/// Extensions for the <see cref="User"/> class.
/// </summary>
public static class UserExtensions
{
    /// <summary>
    /// Convert a <see cref="User"/> to a <see cref="ClaimsPrincipal"/>.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="additionalClaims">Additional claims to include.</param>
    public static ClaimsPrincipal ToPrincipal(this User user, IEnumerable<Claim>? additionalClaims = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role.ToString())
        };
        if (user.ProviderType is not null)
        {
            if (user.ExtensionId.HasValue)
            {
                claims.Add(new(MagpieClaims.AuthProviderExtension, user.ExtensionId.Value.ToString()));
            }
            claims.Add(new(MagpieClaims.AuthProviderType, user.ProviderType));
        }
        if (additionalClaims is not null)
        {
            claims.AddRange(additionalClaims);
        }

        var identity = new ClaimsIdentity(claims, "Magpie");
        var principal = new ClaimsPrincipal(identity);

        return principal;
    }
}
