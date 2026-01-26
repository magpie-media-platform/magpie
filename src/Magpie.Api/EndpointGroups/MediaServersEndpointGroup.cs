using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Data;
using Magpie.Models.MediaServers;
using Magpie.Models.Result;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using HttpResult = Microsoft.AspNetCore.Http.IResult;

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

        builder.MapGet("/by-provider/{provider}", GetMediaServersByProvider)
            .WithName("GetMediaServersByProvider")
            .WithTags("MediaServers")
            .Produces<MediaServer[]>()
            .RequireAuthorization();

        builder.MapGet("/by-status/{status}", GetMediaServersByStatus)
            .WithName("GetMediaServersByStatus")
            .WithTags("MediaServers")
            .Produces<MediaServer[]>()
            .RequireAuthorization();

        builder.MapGet("/enabled", GetEnabledMediaServers)
            .WithName("GetEnabledMediaServers")
            .WithTags("MediaServers")
            .Produces<MediaServer[]>()
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

    private async Task<HttpResult> GetAllMediaServers(CancellationToken cancellationToken)
    {
        var result = await mediaServerRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<MediaServer>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<MediaServer>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetMediaServerById(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediaServerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<MediaServer?> { Value: null } => Results.NotFound(),
            SuccessResult<MediaServer?> success => Results.Ok(success.Value),
            FailureResult<MediaServer?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetMediaServersByProvider(string provider, CancellationToken cancellationToken)
    {
        var result = await mediaServerRepository.GetByProviderAsync(provider, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<MediaServer>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<MediaServer>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetMediaServersByStatus(
        MediaServerStatus status,
        CancellationToken cancellationToken)
    {
        var result = await mediaServerRepository.GetByStatusAsync(status, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<MediaServer>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<MediaServer>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetEnabledMediaServers(CancellationToken cancellationToken)
    {
        var result = await mediaServerRepository.GetEnabledAsync(cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<MediaServer>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<MediaServer>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> CreateMediaServer(MediaServer request, CancellationToken cancellationToken)
    {
        var mediaServer = request with
        {
            Id = Guid.NewGuid(),
            Status = MediaServerStatus.Unknown,
            LastChecked = null,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        var result = await mediaServerRepository.CreateAsync(mediaServer, createdByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<MediaServer> success =>
                Results.Created($"/api/media-servers/{success.Value.Id}", success.Value),
            FailureResult<MediaServer> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status400BadRequest),
            _ =>
                Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> PatchMediaServer(
        Guid id,
        MediaServerPatch request,
        CancellationToken cancellationToken)
    {
        var patch = request with { Id = id };

        var result = await mediaServerRepository.PatchAsync(patch, modifiedByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<bool> { Value: false } => Results.NotFound(),
            SuccessResult<bool> => Results.NoContent(),
            FailureResult<bool> failure => Results.Problem(failure.Reason, statusCode: StatusCodes.Status400BadRequest),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> DeleteMediaServer(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediaServerRepository.DeleteAsync(id, deletedByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<bool> { Value: false } => Results.NotFound(),
            SuccessResult<bool> => Results.NoContent(),
            FailureResult<bool> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}
