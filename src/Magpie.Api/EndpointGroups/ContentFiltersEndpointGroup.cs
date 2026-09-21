using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class ContentFiltersEndpointGroup : IApiEndpointGroup
{
    public string Name => "content-filters";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetContentFiltersAsync)
            .WithName(nameof(GetContentFiltersAsync))
            .Produces<IReadOnlyList<ContentFilter>>()
            .RequireViewer();

        builder.MapPost("", CreateContentFilterAsync)
            .WithName(nameof(CreateContentFilterAsync))
            .Produces<ContentFilter>(StatusCodes.Status201Created)
            .RequireViewer();

        builder.MapGet("my", GetMyContentFiltersAsync)
            .WithName(nameof(GetMyContentFiltersAsync))
            .Produces<IReadOnlyList<ContentFilter>>()
            .RequireViewer();

        builder.MapGet("{id:guid}", GetContentFilterAsync)
            .WithName(nameof(GetContentFilterAsync))
            .Produces<ContentFilter>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPatch("{id:guid}", PatchContentFilterAsync)
            .WithName(nameof(PatchContentFilterAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapDelete("{id:guid}", DeleteContentFilterAsync)
            .WithName(nameof(DeleteContentFilterAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();
    }

    internal async Task<IResult> GetContentFiltersAsync(
        [FromServices] IFilterService filterService,
        AuthenticatedUser user,
        string? name,
        string? tag,
        CancellationToken cancellationToken)
    {
        var filters = await filterService.ListVisibleContentFiltersAsync(name, tag, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(filters);
    }

    internal async Task<IResult> CreateContentFilterAsync(
        [FromServices] IFilterService filterService,
        AuthenticatedUser user,
        ContentFilter filter,
        CancellationToken cancellationToken)
    {
        var created = await filterService.CreateContentFilterAsync(filter, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"/api/{Name}/{created.Id}", created);
    }

    internal async Task<IResult> GetMyContentFiltersAsync(
        [FromServices] IFilterService filterService,
        AuthenticatedUser user,
        CancellationToken cancellationToken)
    {
        var filters = await filterService.ListOwnContentFiltersAsync(user.Id, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(filters);
    }

    internal async Task<IResult> GetContentFilterAsync(
        [FromServices] IFilterService filterService,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        var filter = await filterService
            .GetContentFilterAsync(id, user.Id, user.IsInRole(Role.Admin), cancellationToken)
            .ConfigureAwait(false);

        return filter is null ? Results.NotFound() : Results.Ok(filter);
    }

    internal async Task<IResult> PatchContentFilterAsync(
        [FromServices] IFilterService filterService,
        AuthenticatedUser user,
        Guid id,
        ContentFilterPatch patch,
        CancellationToken cancellationToken)
    {
        var patched = await filterService
            .PatchContentFilterAsync(id, patch, user.Id, user.IsInRole(Role.Admin), cancellationToken)
            .ConfigureAwait(false);

        return patched ? Results.NoContent() : Results.NotFound();
    }

    internal async Task<IResult> DeleteContentFilterAsync(
        [FromServices] IFilterService filterService,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        var deleted = await filterService
            .DeleteContentFilterAsync(id, user.Id, user.IsInRole(Role.Admin), cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }
}

