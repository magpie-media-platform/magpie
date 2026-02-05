using System.Reflection;
using System.Runtime.InteropServices;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.System;
using Magpie.Models.Api;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class SystemEndpointGroup(IMagpieSystem magpieSystem) : IApiEndpointGroup
{
    public string Name => "system";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("health", GetSystemHealth)
            .WithName(nameof(GetSystemHealth))
            .Produces<HealthResponse>()
            .AllowAnonymous();

        builder.MapGet("info", GetSystemInfo)
            .WithName(nameof(GetSystemInfo))
            .Produces<SystemInfo>()
            .RequireViewer();
    }

    private static IResult GetSystemHealth()
    {
        return Results.Ok(new HealthResponse
        {
            Status = "Healthy"
        });
    }

    private IResult GetSystemInfo()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString() ?? "0.0.0";

        return Results.Ok(new SystemInfo()
        {
            Version = version,
            Runtime = RuntimeInformation.FrameworkDescription,
            OperatingSystem = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            SystemRoot = magpieSystem.SystemRoot,
            NodeId = magpieSystem.NodeId
        });
    }
}
