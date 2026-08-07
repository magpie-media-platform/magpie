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

[MagpieCommandGroup("root-folders", "root-folders", "Manage root folders")]
internal sealed class RootFoldersEndpointGroup : IApiEndpointGroup
{
    public string Name => "root-folders";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetRootFoldersAsync)
            .WithName(nameof(GetRootFoldersAsync))
            .Produces<IReadOnlyList<RootFolder>>()
            .RequireViewer();

        builder.MapPost("", CreateRootFolderAsync)
            .WithName(nameof(CreateRootFolderAsync))
            .Produces<RootFolder>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAdmin();

        builder.MapGet("{id:guid}", GetRootFolderAsync)
            .WithName(nameof(GetRootFolderAsync))
            .Produces<RootFolder>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPatch("{id:guid}", PatchRootFolderAsync)
            .WithName(nameof(PatchRootFolderAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAdmin();

        builder.MapDelete("{id:guid}", DeleteRootFolderAsync)
            .WithName(nameof(DeleteRootFolderAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();
    }

    [MagpieCommand("list", "GET", "", "List root folders")]
    internal async Task<IResult> GetRootFoldersAsync(
        [FromServices] IRootFolderService rootFolderService,
        CancellationToken cancellationToken)
    {
        var rootFolders = await rootFolderService.ListAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(rootFolders);
    }

    [MagpieCommand("create", "POST", "", "Create a root folder")]
    internal async Task<IResult> CreateRootFolderAsync(
        [FromServices] IRootFolderService rootFolderService,
        AuthenticatedUser user,
        [FromBody] RootFolderCreate create,
        CancellationToken cancellationToken)
    {
        var created = await rootFolderService.CreateAsync(create, user.Id, cancellationToken).ConfigureAwait(false);

        return Results.Created($"api/{Name}/{created.Id}", created);
    }

    [MagpieCommand("get", "GET", "{id:guid}", "Show a root folder")]
    internal async Task<IResult> GetRootFolderAsync(
        [FromServices] IRootFolderService rootFolderService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var rootFolder = await rootFolderService.GetAsync(id, cancellationToken).ConfigureAwait(false);

        return rootFolder is null ? Results.NotFound() : Results.Ok(rootFolder);
    }

    [MagpieCommand("patch", "PATCH", "{id:guid}", "Update fields on a root folder")]
    internal async Task<IResult> PatchRootFolderAsync(
        [FromServices] IRootFolderService rootFolderService,
        AuthenticatedUser user,
        Guid id,
        [FromBody] RootFolderPatch patch,
        CancellationToken cancellationToken)
    {
        var updated = await rootFolderService.PatchAsync(id, patch, user.Id, cancellationToken).ConfigureAwait(false);

        return updated ? Results.NoContent() : Results.NotFound();
    }

    [MagpieCommand("delete", "DELETE", "{id:guid}", "Delete a root folder")]
    internal async Task<IResult> DeleteRootFolderAsync(
        [FromServices] IRootFolderService rootFolderService,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        var deleted = await rootFolderService.DeleteAsync(id, user.Id, cancellationToken).ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
