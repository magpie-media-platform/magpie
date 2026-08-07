using Magpie.Api.Setup;
using Magpie.Sdk.Api;

using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130
namespace Microsoft.AspNetCore.Builder;
#pragma warning restore IDE0130

/// <summary>
/// The application extensions that add Magpie to the ASP.NET Core request pipeline.
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// Registers the middleware that blocks API requests until setup is complete.
    /// </summary>
    /// <param name="app">The web application to configure</param>
    public static WebApplication UseMagpieSetupRedirect(this WebApplication app)
    {
        app.UseMiddleware<SetupRedirectMiddleware>();

        return app;
    }

    /// <summary>
    /// Maps the Magpie API endpoint groups under the <c>/api</c> route.
    /// </summary>
    /// <param name="app">The web application to configure</param>
    public static WebApplication MapMagpieEndpoints(this WebApplication app)
    {
        var apiGroup = app.MapGroup("/api");

        foreach (var group in app.Services.GetServices<IApiEndpointGroup>())
        {
            var groupRoute = apiGroup.MapGroup(group.Name);

            group.MapEndpoints(groupRoute);
        }

        return app;
    }
}
