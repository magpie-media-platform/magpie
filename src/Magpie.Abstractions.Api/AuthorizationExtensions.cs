using Magpie.Models.Enums;

using Microsoft.AspNetCore.Builder;

namespace Magpie.Abstractions.Api;

/// <summary>
/// Extension methods for applying role-based authorization to endpoints.
/// </summary>
public static class AuthorizationExtensions
{
    /// <summary>
    /// Requires that the user has at least Viewer role (the minimum access level).
    /// </summary>
    public static RouteHandlerBuilder RequireViewer(this RouteHandlerBuilder builder)
    {
        return builder.RequireAuthorization(nameof(Role.Viewer));
    }

    /// <summary>
    /// Requires that the user has at least Requester role.
    /// </summary>
    public static RouteHandlerBuilder RequireRequester(this RouteHandlerBuilder builder)
    {
        return builder.RequireAuthorization(nameof(Role.Requester));
    }

    /// <summary>
    /// Requires that the user has at least Manager role.
    /// </summary>
    public static RouteHandlerBuilder RequireManager(this RouteHandlerBuilder builder)
    {
        return builder.RequireAuthorization(nameof(Role.Manager));
    }

    /// <summary>
    /// Requires that the user has Admin role.
    /// </summary>
    public static RouteHandlerBuilder RequireAdmin(this RouteHandlerBuilder builder)
    {
        return builder.RequireAuthorization(nameof(Role.Admin));
    }

    /// <summary>
    /// Requires that the user has at least Viewer role (the minimum access level).
    /// </summary>
    public static TBuilder RequireViewer<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization(nameof(Role.Viewer));
    }

    /// <summary>
    /// Requires that the user has at least Requester role.
    /// </summary>
    public static TBuilder RequireRequester<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization(nameof(Role.Requester));
    }

    /// <summary>
    /// Requires that the user has at least Manager role.
    /// </summary>
    public static TBuilder RequireManager<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization(nameof(Role.Manager));
    }

    /// <summary>
    /// Requires that the user has Admin role.
    /// </summary>
    public static TBuilder RequireAdmin<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization(nameof(Role.Admin));
    }
}
