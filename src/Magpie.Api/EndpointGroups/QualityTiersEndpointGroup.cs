using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

[MagpieCommandGroup("quality-tiers", "quality-tiers", "Manage quality tiers")]
internal sealed class QualityTiersEndpointGroup : IApiEndpointGroup
{
    public string Name => "quality-tiers";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetQualityTiersAsync)
            .WithName(nameof(GetQualityTiersAsync))
            .Produces<IReadOnlyList<QualityTier>>()
            .RequireViewer();

        builder.MapPost("", CreateQualityTierAsync)
            .WithName(nameof(CreateQualityTierAsync))
            .Produces<QualityTier>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAdmin();

        builder.MapGet("{id:guid}", GetQualityTierAsync)
            .WithName(nameof(GetQualityTierAsync))
            .Produces<QualityTier>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPatch("{id:guid}", PatchQualityTierAsync)
            .WithName(nameof(PatchQualityTierAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapDelete("{id:guid}", DeleteQualityTierAsync)
            .WithName(nameof(DeleteQualityTierAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();
    }

    [MagpieCommand("list", "GET", "", "List quality tiers")]
    internal async Task<IResult> GetQualityTiersAsync(
        [FromServices] IQualityService qualityService,
        CancellationToken cancellationToken)
    {
        var tiers = await qualityService.ListTiersAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(tiers);
    }

    [MagpieCommand("create", "POST", "", "Create a quality tier")]
    internal async Task<IResult> CreateQualityTierAsync(
        [FromServices] IQualityService qualityService,
        AuthenticatedUser user,
        [FromBody] QualityTier request,
        CancellationToken cancellationToken)
    {
        var created = await qualityService.CreateTierAsync(request, user.Id, cancellationToken).ConfigureAwait(false);

        return Results.Created($"/api/{Name}/{created.Id}", created);
    }

    [MagpieCommand("get", "GET", "{id:guid}", "Show a quality tier")]
    internal async Task<IResult> GetQualityTierAsync(
        [FromServices] IQualityService qualityService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var tier = await qualityService.GetTierAsync(id, cancellationToken).ConfigureAwait(false);

        return tier is null ? Results.NotFound() : Results.Ok(tier);
    }

    [MagpieCommand("patch", "PATCH", "{id:guid}", "Update fields on a quality tier")]
    internal async Task<IResult> PatchQualityTierAsync(
        [FromServices] IQualityService qualityService,
        AuthenticatedUser user,
        Guid id,
        [FromBody] QualityTierPatch patch,
        CancellationToken cancellationToken)
    {
        var updated = await qualityService.PatchTierAsync(id, patch, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return updated ? Results.NoContent() : Results.NotFound();
    }

    [MagpieCommand("delete", "DELETE", "{id:guid}", "Delete a quality tier")]
    internal async Task<IResult> DeleteQualityTierAsync(
        [FromServices] IQualityService qualityService,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        var deleted = await qualityService.DeleteTierAsync(id, user.Id, cancellationToken).ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
