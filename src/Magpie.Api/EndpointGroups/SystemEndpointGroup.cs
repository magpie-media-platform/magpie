using System.Reflection;
using System.Runtime.InteropServices;

using Magpie.Abstractions.Api;
using Magpie.Models.System;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class SystemEndpointGroup : IApiEndpointGroup
{
    public string Name => "system";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/health", GetHealth)
            .WithName("GetHealth")
            .WithTags("System")
            .Produces<HealthStatus>()
            .AllowAnonymous();

        builder.MapGet("/info", GetInfo)
            .WithName("GetInfo")
            .WithTags("System")
            .Produces<SystemInfo>()
            .RequireAuthorization();
    }

    private static IResult GetHealth()
    {
        return Results.Ok(new HealthStatus("healthy"));
    }

    private static IResult GetInfo()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString() ?? "0.0.0";
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? version;

        return Results.Ok(new SystemInfo(
            informationalVersion,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString()));
    }
}
