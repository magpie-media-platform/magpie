using Magpie.Models.Enums;

using Microsoft.AspNetCore.Builder;

namespace Magpie.Sdk.Api;

/// <summary>
/// Extension methods for role-based endpoint authorization.
/// </summary>
public static class AuthorizationExtensions
{
    /// <summary>
    /// Requires at least the Viewer role.
    /// </summary>
    /// <param name="builder">The builder of the endpoint to gate</param>
    /// <returns>The same builder.</returns>
    public static RouteHandlerBuilder RequireViewer(this RouteHandlerBuilder builder)
    {
        return builder.RequireAuthorization(nameof(Role.Viewer));
    }

    /// <summary>
    /// Requires at least the Requester role.
    /// </summary>
    /// <param name="builder">The builder of the endpoint to gate</param>
    /// <returns>The same builder.</returns>
    public static RouteHandlerBuilder RequireRequester(this RouteHandlerBuilder builder)
    {
        return builder.RequireAuthorization(nameof(Role.Requester));
    }

    /// <summary>
    /// Requires at least the Manager role.
    /// </summary>
    /// <param name="builder">The builder of the endpoint to gate</param>
    /// <returns>The same builder.</returns>
    public static RouteHandlerBuilder RequireManager(this RouteHandlerBuilder builder)
    {
        return builder.RequireAuthorization(nameof(Role.Manager));
    }

    /// <summary>
    /// Requires the Admin role.
    /// </summary>
    /// <param name="builder">The builder of the endpoint to gate</param>
    /// <returns>The same builder.</returns>
    public static RouteHandlerBuilder RequireAdmin(this RouteHandlerBuilder builder)
    {
        return builder.RequireAuthorization(nameof(Role.Admin));
    }

    /// <summary>
    /// Requires at least the Viewer role.
    /// </summary>
    /// <typeparam name="TBuilder">The type of the endpoint builder</typeparam>
    /// <param name="builder">The builder of the endpoint to gate</param>
    /// <returns>The same builder.</returns>
    public static TBuilder RequireViewer<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization(nameof(Role.Viewer));
    }

    /// <summary>
    /// Requires at least the Requester role.
    /// </summary>
    /// <typeparam name="TBuilder">The type of the endpoint builder</typeparam>
    /// <param name="builder">The builder of the endpoint to gate</param>
    /// <returns>The same builder.</returns>
    public static TBuilder RequireRequester<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization(nameof(Role.Requester));
    }

    /// <summary>
    /// Requires at least the Manager role.
    /// </summary>
    /// <typeparam name="TBuilder">The type of the endpoint builder</typeparam>
    /// <param name="builder">The builder of the endpoint to gate</param>
    /// <returns>The same builder.</returns>
    public static TBuilder RequireManager<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization(nameof(Role.Manager));
    }

    /// <summary>
    /// Requires the Admin role.
    /// </summary>
    /// <typeparam name="TBuilder">The type of the endpoint builder</typeparam>
    /// <param name="builder">The builder of the endpoint to gate</param>
    /// <returns>The same builder.</returns>
    public static TBuilder RequireAdmin<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization(nameof(Role.Admin));
    }
}
