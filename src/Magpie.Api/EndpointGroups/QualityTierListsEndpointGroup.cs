using System;
using System.Collections.Generic;
using System.Text.Json;
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

[MagpieCommandGroup("quality-tier-lists", "quality-tier-lists", "Manage quality tier lists")]
internal sealed class QualityTierListsEndpointGroup : IApiEndpointGroup
{
    public string Name => "quality-tier-lists";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetQualityTierListsAsync)
            .WithName(nameof(GetQualityTierListsAsync))
            .Produces<IReadOnlyList<QualityTierList>>()
            .RequireViewer();

        builder.MapPost("", CreateQualityTierListAsync)
            .WithName(nameof(CreateQualityTierListAsync))
            .Produces<QualityTierList>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAdmin();

        builder.MapGet("{id:guid}", GetQualityTierListAsync)
            .WithName(nameof(GetQualityTierListAsync))
            .Produces<QualityTierList>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPatch("{id:guid}", PatchQualityTierListAsync)
            .WithName(nameof(PatchQualityTierListAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapDelete("{id:guid}", DeleteQualityTierListAsync)
            .WithName(nameof(DeleteQualityTierListAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();
    }

    [MagpieCommand("list", "GET", "", "List quality tier lists")]
    internal async Task<IResult> GetQualityTierListsAsync(
        [FromServices] IQualityService qualityService,
        CancellationToken cancellationToken)
    {
        var tierLists = await qualityService.ListTierListsAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(tierLists);
    }

    [MagpieCommand("create", "POST", "", "Create a quality tier list")]
    internal async Task<IResult> CreateQualityTierListAsync(
        [FromServices] IQualityService qualityService,
        AuthenticatedUser user,
        [FromBody] QualityTierList request,
        CancellationToken cancellationToken)
    {
        var result = await qualityService.CreateTierListAsync(request, user.Id, cancellationToken)
            .ConfigureAwait(false);
        if (result.ValidationError is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [JsonNamingPolicy.CamelCase.ConvertName(nameof(QualityTierList.MimeTypes))] = [result.ValidationError]
            });
        }

        return Results.Created($"/api/{Name}/{result.TierList!.Id}", result.TierList);
    }

    [MagpieCommand("get", "GET", "{id:guid}", "Show a quality tier list")]
    internal async Task<IResult> GetQualityTierListAsync(
        [FromServices] IQualityService qualityService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var tierList = await qualityService.GetTierListAsync(id, cancellationToken).ConfigureAwait(false);

        return tierList is null ? Results.NotFound() : Results.Ok(tierList);
    }

    [MagpieCommand("patch", "PATCH", "{id:guid}", "Update fields on a quality tier list")]
    internal async Task<IResult> PatchQualityTierListAsync(
        [FromServices] IQualityService qualityService,
        AuthenticatedUser user,
        Guid id,
        [FromBody] QualityTierListPatch patch,
        CancellationToken cancellationToken)
    {
        var result = await qualityService.PatchTierListAsync(id, patch, user.Id, cancellationToken)
            .ConfigureAwait(false);
        if (result.ValidationError is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [JsonNamingPolicy.CamelCase.ConvertName(nameof(QualityTierList.MimeTypes))] = [result.ValidationError]
            });
        }

        return result.Updated ? Results.NoContent() : Results.NotFound();
    }

    [MagpieCommand("delete", "DELETE", "{id:guid}", "Delete a quality tier list")]
    internal async Task<IResult> DeleteQualityTierListAsync(
        [FromServices] IQualityService qualityService,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        var deleted = await qualityService.DeleteTierListAsync(id, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
