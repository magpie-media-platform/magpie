using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Api.Hosting;
using Magpie.Api.Setup;
using Magpie.Models;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

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
    /// Registers the database migration gate middleware.
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
        app.MapMagpieHealthEndpoints();
        var apiGroup = app.MapGroup("/api");

        foreach (var group in app.Services.GetServices<IApiEndpointGroup>())
        {
            var groupRoute = apiGroup.MapGroup(group.Name);

            group.MapEndpoints(groupRoute);
        }

        var dataSources = ((IEndpointRouteBuilder)app).DataSources;
        apiGroup.MapFallback(
            "{**rest}",
            (HttpContext context, [FromServices] ParameterPolicyFactory policyFactory) =>
                AnswerUnroutedApiRequest(context, dataSources, policyFactory))
            .AllowAnonymous();

        return app;
    }

    /// <summary>
    /// Maps the Magpie health and readiness endpoints under <c>/api/system</c>.
    /// </summary>
    /// <param name="app">The web application to configure</param>
    public static WebApplication MapMagpieHealthEndpoints(this WebApplication app)
    {
        var systemGroup = app.MapGroup("/api/system");
        systemGroup.MapGet("health", GetSystemHealthAsync)
            .WithName(nameof(GetSystemHealthAsync))
            .Produces<SystemHealth>()
            .AllowAnonymous();
        systemGroup.MapGet("ready", GetSystemReadiness)
            .WithName(nameof(GetSystemReadiness))
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .AllowAnonymous();

        return app;
    }

    private static async Task<IResult> GetSystemHealthAsync(
        [FromServices] IMagpieSystem magpieSystem,
        CancellationToken cancellationToken)
    {
        var health = await magpieSystem.CheckHealthAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(health);
    }

    private static IResult GetSystemReadiness([FromServices] DatabaseMigrationState migrationState)
    {
        if (migrationState.IsSchemaKnown)
        {
            return Results.Ok();
        }

        var detail = migrationState.UnknownMigrations.Count > 0
            ? "The database schema is newer than the current node, the node requires an upgrade."
            : "The node is starting.";
        return Results.Problem(
            detail: detail,
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "The node is not ready.");
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
                TemplateParser.Parse(template),
                new RouteValueDictionary(endpoint.RoutePattern.Defaults));
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
