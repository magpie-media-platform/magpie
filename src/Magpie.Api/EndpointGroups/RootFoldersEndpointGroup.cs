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
            .WithName("GetAllRootFolders")
            .WithTags("RootFolders")
            .Produces<RootFolder[]>()
            .RequireAuthorization();

        builder.MapGet("/{id:guid}", GetRootFolderById)
            .WithName("GetRootFolderById")
            .WithTags("RootFolders")
            .Produces<RootFolder>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapPost("", CreateRootFolder)
            .WithName("CreateRootFolder")
            .WithTags("RootFolders")
            .Produces<RootFolder>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapPut("/{id:guid}", UpdateRootFolder)
            .WithName("UpdateRootFolder")
            .WithTags("RootFolders")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapDelete("/{id:guid}", DeleteRootFolder)
            .WithName("DeleteRootFolder")
            .WithTags("RootFolders")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();
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

        return Results.Created($"/api/root-folders/{created.Id}", created);
    }

    private async Task<IResult> UpdateRootFolder(
        HttpContext httpContext,
        Guid id,
        RootFolder request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var rootFolder = request with
        {
            Id = id,
            ModifiedAt = DateTime.UtcNow,
            ModifiedByUserId = currentUserId
        };

        var updated = await rootFolderRepository.UpdateAsync(rootFolder, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return updated
            ? Results.NoContent()
            : Results.NotFound();
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
