using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Models.Data;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class RootFoldersEndpointGroup(
    IRootFolderRepository rootFolderRepository) : IApiEndpointGroup
{
    public string Name => "root-folders";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetAllRootFolders)
            .WithName(nameof(GetAllRootFolders))
            .Produces<RootFolder[]>()
            .RequireViewer();

        builder.MapGet("{id:guid}", GetRootFolderById)
            .WithName(nameof(GetRootFolderById))
            .Produces<RootFolder>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPost("", CreateRootFolder)
            .WithName(nameof(CreateRootFolder))
            .Produces<RootFolder>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapPatch("{id:guid}", PatchRootFolder)
            .WithName(nameof(PatchRootFolder))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapDelete("{id:guid}", DeleteRootFolder)
            .WithName(nameof(DeleteRootFolder))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();
    }

    private async Task<IResult> GetAllRootFolders(CancellationToken cancellationToken)
    {
        var rootFolders = await rootFolderRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(rootFolders);
    }

    private async Task<IResult> GetRootFolderById(Guid id, CancellationToken cancellationToken)
    {
        var rootFolder = await rootFolderRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return rootFolder is null
            ? Results.NotFound()
            : Results.Ok(rootFolder);
    }

    private async Task<IResult> CreateRootFolder(
        HttpContext httpContext,
        RootFolder request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var rootFolder = request with
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = currentUserId,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        var created = await rootFolderRepository.CreateAsync(rootFolder, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"api/{Name}/{created.Id}", created);
    }

    private async Task<IResult> PatchRootFolder(
        HttpContext httpContext,
        Guid id,
        RootFolder request,
        CancellationToken cancellationToken)
    {
        var existing = await rootFolderRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            return Results.NotFound();
        }

        var currentUserId = httpContext.User.GetUserId();

        var rootFolder = request with
        {
            Id = id,
            CreatedAt = existing.CreatedAt,
            CreatedByUserId = existing.CreatedByUserId,
            ModifiedAt = DateTime.UtcNow,
            ModifiedByUserId = currentUserId
        };

        await rootFolderRepository.UpdateAsync(rootFolder, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.NoContent();
    }

    private async Task<IResult> DeleteRootFolder(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var deleted = await rootFolderRepository.DeleteAsync(id, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return deleted
            ? Results.NoContent()
            : Results.NotFound();
    }
}
