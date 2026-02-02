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
            .WithName("GetAllMediaServers")
            .WithTags("MediaServers")
            .Produces<MediaServer[]>()
            .RequireAuthorization();

        builder.MapGet("/{id:guid}", GetMediaServerById)
            .WithName("GetMediaServerById")
            .WithTags("MediaServers")
            .Produces<MediaServer>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapPost("", CreateMediaServer)
            .WithName("CreateMediaServer")
            .WithTags("MediaServers")
            .Produces<MediaServer>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapPatch("/{id:guid}", PatchMediaServer)
            .WithName("PatchMediaServer")
            .WithTags("MediaServers")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapDelete("/{id:guid}", DeleteMediaServer)
            .WithName("DeleteMediaServer")
            .WithTags("MediaServers")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();
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

        return Results.Created($"/api/media-servers/{created.Id}", created);
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
