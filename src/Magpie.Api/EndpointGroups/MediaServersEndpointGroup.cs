using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class MediaServersEndpointGroup(IMediaServerRepository mediaServerRepository) : IApiEndpointGroup
{
    public string Name => "media-servers";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetAllMediaServers)
            .WithName(nameof(GetAllMediaServers))
            .Produces<MediaServer[]>()
            .RequireViewer();

        builder.MapGet("{id:guid}", GetMediaServerById)
            .WithName(nameof(GetMediaServerById))
            .Produces<MediaServer>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPost("", CreateMediaServer)
            .WithName(nameof(CreateMediaServer))
            .Produces<MediaServer>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapPatch("{id:guid}", PatchMediaServer)
            .WithName(nameof(PatchMediaServer))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapPost("{id:guid}/enable", EnableMediaServer)
            .WithName(nameof(EnableMediaServer))
            .Produces<MediaServer>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapPost("{id:guid}/disable", DisableMediaServer)
            .WithName(nameof(DisableMediaServer))
            .Produces<MediaServer>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapDelete("{id:guid}", DeleteMediaServer)
            .WithName(nameof(DeleteMediaServer))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();
    }

    private async Task<IResult> GetAllMediaServers(CancellationToken cancellationToken)
    {
        var mediaServers = await mediaServerRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(mediaServers);
    }

    private async Task<IResult> GetMediaServerById(Guid id, CancellationToken cancellationToken)
    {
        var mediaServer = await mediaServerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return mediaServer is null
            ? Results.NotFound()
            : Results.Ok(mediaServer);
    }

    private async Task<IResult> CreateMediaServer(
        HttpContext httpContext,
        MediaServer request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var mediaServer = request with
        {
            Id = Guid.NewGuid(),
            Status = MediaServerStatus.Unknown,
            LastChecked = null,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = currentUserId,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        var created = await mediaServerRepository.CreateAsync(mediaServer, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"api/{Name}/{created.Id}", created);
    }

    private async Task<IResult> PatchMediaServer(
        HttpContext httpContext,
        Guid id,
        MediaServerPatch request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var patch = request with { Id = id };

        var updated = await mediaServerRepository.PatchAsync(patch, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return updated
            ? Results.NoContent()
            : Results.NotFound();
    }

    private async Task<IResult> EnableMediaServer(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var existing = await mediaServerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            return Results.NotFound();
        }

        var currentUserId = httpContext.User.GetUserId();

        var mediaServer = existing with
        {
            Enabled = true,
            ModifiedAt = DateTime.UtcNow,
            ModifiedByUserId = currentUserId
        };

        await mediaServerRepository.UpdateAsync(mediaServer, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(mediaServer);
    }

    private async Task<IResult> DisableMediaServer(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var existing = await mediaServerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            return Results.NotFound();
        }

        var currentUserId = httpContext.User.GetUserId();

        var mediaServer = existing with
        {
            Enabled = false,
            ModifiedAt = DateTime.UtcNow,
            ModifiedByUserId = currentUserId
        };

        await mediaServerRepository.UpdateAsync(mediaServer, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(mediaServer);
    }

    private async Task<IResult> DeleteMediaServer(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var deleted = await mediaServerRepository.DeleteAsync(id, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return deleted
            ? Results.NoContent()
            : Results.NotFound();
    }
}
