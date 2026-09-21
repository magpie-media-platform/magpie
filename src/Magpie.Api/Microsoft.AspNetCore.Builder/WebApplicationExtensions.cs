using System;
using System.Collections.Generic;
using System.Linq;

using Magpie.Api.Hosting;
using Magpie.Api.Setup;
using Magpie.Sdk.Api;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
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
    /// Registers the middleware that blocks requests until the database work at start is complete.
    /// </summary>
    /// <param name="app">The web application to configure</param>
    public static WebApplication UseMagpieDatabaseMigrationGate(this WebApplication app)
    {
        app.UseMiddleware<DatabaseMigrationMiddleware>();

        return app;
    }

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
    /// Maps the Magpie API endpoint groups under <c>/api</c> and answers every other <c>/api</c> path with a 404.
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

        var dataSources = ((IEndpointRouteBuilder)app).DataSources;
        apiGroup.MapFallback("{**rest}", (HttpContext context, [FromServices] ParameterPolicyFactory policyFactory) =>
            AnswerUnroutedApiRequest(context, dataSources, policyFactory))
            .AllowAnonymous();

        return app;
    }

    private static IResult AnswerUnroutedApiRequest(
        HttpContext context,
        ICollection<EndpointDataSource> dataSources,
        ParameterPolicyFactory policyFactory)
    {
        var allowedMethods = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var endpoint in dataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>())
        {
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
            if (endpoint.Order == int.MaxValue || methods is null ||
                endpoint.RoutePattern.RawText is not { } template)
            {
                continue;
            }

            var matcher = new TemplateMatcher(
                TemplateParser.Parse(template), new RouteValueDictionary(endpoint.RoutePattern.Defaults));
            var values = new RouteValueDictionary();
            if (matcher.TryMatch(context.Request.Path, values) &&
                endpoint.RoutePattern.ParameterPolicies.All(parameter => parameter.Value.All(policy =>
                    policyFactory.Create(endpoint.RoutePattern.GetParameter(parameter.Key), policy)
                        is not IRouteConstraint constraint ||
                    constraint.Match(context, null, parameter.Key, values, RouteDirection.IncomingRequest))))
            {
                allowedMethods.UnionWith(methods.HttpMethods);
            }
        }

        if (allowedMethods.Count == 0)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound);
        }

        context.Response.Headers.Allow = string.Join(", ", allowedMethods);

        return Results.Problem(statusCode: StatusCodes.Status405MethodNotAllowed);
    }
}
