using System;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;

using Magpie.Models.Enums;

using Microsoft.AspNetCore.Http;

namespace Magpie.Sdk.Api;

/// <summary>
/// The authenticated user of a request.
/// </summary>
/// <param name="Id">The id of the authenticated user</param>
/// <param name="Role">The highest role that the user holds</param>
public sealed record AuthenticatedUser(Guid Id, Role Role = Role.Viewer)
{
    /// <summary>
    /// Binds the authenticated user from the HTTP context.
    /// </summary>
    /// <param name="context">The HTTP context of the current request</param>
    /// <param name="parameter">The parameter that receives the bound user</param>
    /// <returns>The authenticated user, or <see langword="null"/> for an unauthenticated request.</returns>
    public static ValueTask<AuthenticatedUser?> BindAsync(HttpContext context, ParameterInfo parameter)
    {
        var claim = context.User.FindFirst(ClaimTypes.NameIdentifier);
        if (claim is null || !Guid.TryParse(claim.Value, out var id))
        {
            return ValueTask.FromResult<AuthenticatedUser?>(null);
        }

        var role = HighestRole(context.User);
        return ValueTask.FromResult<AuthenticatedUser?>(new AuthenticatedUser(id, role));
    }

    private static Role HighestRole(ClaimsPrincipal principal)
    {
        var highest = Role.Viewer;
        foreach (var claim in principal.FindAll(ClaimTypes.Role))
        {
            if (int.TryParse(claim.Value, out _))
            {
                continue;
            }

            if (Enum.TryParse<Role>(claim.Value, ignoreCase: true, out var parsed) &&
                Enum.IsDefined(parsed) &&
                parsed > highest)
            {
                highest = parsed;
            }
        }

        return highest;
    }
    /// <summary>
    /// Determines whether the user holds at least the required role.
    /// </summary>
    /// <param name="required">The minimum role that the user must hold</param>
    /// <returns><see langword="true"/> for a user with at least this role, otherwise <see langword="false"/>.</returns>
    public bool IsInRole(Role required) => Role >= required;
}
