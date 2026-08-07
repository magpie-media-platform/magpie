using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

[MagpieCommandGroup("diagnostics", "diagnostics", "Diagnose system configuration issues")]
internal sealed class DiagnosticEndpointGroup : IApiEndpointGroup
{
    public string Name => "diagnostics";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("findings", GetFindingsAsync)
            .WithName(nameof(GetFindingsAsync))
            .Produces<DiagnosticFindings>()
            .RequireViewer();

        builder.MapPost("findings/{findingId:guid}/repair", RepairFindingAsync)
            .WithName(nameof(RepairFindingAsync))
            .Produces<DiagnosticRepairResult>()
            .RequireAdmin();
    }

    [MagpieCommand("findings", "GET", "findings", "List diagnostic findings")]
    internal async Task<IResult> GetFindingsAsync(
        [FromServices] IDiagnosticsService diagnosticsService,
        CancellationToken cancellationToken)
    {
        var findings = await diagnosticsService.GetFindingsAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(findings);
    }

    [MagpieCommand("repair", "POST", "findings/{findingId:guid}/repair", "Repair a diagnostic finding")]
    internal async Task<IResult> RepairFindingAsync(
        [FromServices] IDiagnosticsService diagnosticsService,
        Guid findingId,
        CancellationToken cancellationToken)
    {
        var result = await diagnosticsService.RepairAsync(findingId, cancellationToken).ConfigureAwait(false);

        return Results.Ok(result);
    }
}
