using System;

using Magpie.Abstractions.Api;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Magpie.Api;

/// <summary>
/// Maps every registered <see cref="IApiEndpointGroup"/> under <c>/api/{group-name}</c>.
/// </summary>
public static class MagpieEndpointMapping
{
    /// <summary>
    /// Resolve every <see cref="IApiEndpointGroup"/> in the host services and call
    /// <see cref="IApiEndpointGroup.MapEndpoints"/> for each one under
    /// <c>/api/{group.Name}</c>. Groups whose dependencies cannot be resolved (e.g. an
    /// endpoint that requires a service contributed by an unloaded extension) are
    /// skipped with a warning so the rest of the host can still come up.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The web application for chaining.</returns>
    public static WebApplication MapMagpieEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Magpie.Api.EndpointMapping");
        var apiGroup = app.MapGroup("/api");

        foreach (var groupType in MagpieApiServiceCollectionExtensions.EndpointGroupTypes)
        {
            IApiEndpointGroup? group;
            try
            {
                group = app.Services.GetService(groupType) as IApiEndpointGroup;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Skipped endpoint group '{GroupType}' because its dependencies could not be resolved.",
                    groupType.Name);
                continue;
            }

            if (group is null)
            {
                logger.LogWarning("Endpoint group '{GroupType}' was not registered; skipping.", groupType.Name);
                continue;
            }

            try
            {
                var groupRoute = apiGroup.MapGroup(group.Name);
                group.MapEndpoints(groupRoute);
                logger.LogDebug("Mapped endpoint group '{GroupName}' ({GroupType})", group.Name, groupType.Name);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to map endpoint group '{GroupName}' ({GroupType})",
                    group.Name,
                    groupType.Name);
            }
        }

        return app;
    }
}
