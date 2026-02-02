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

internal sealed class DownloadClientsEndpointGroup(
    IDownloadClientRepository downloadClientRepository) : IApiEndpointGroup
{
    public string Name => "download-clients";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetAllDownloadClients)
            .WithName("GetAllDownloadClients")
            .WithTags("DownloadClients")
            .Produces<DownloadClient[]>()
            .RequireAuthorization();

        builder.MapGet("/{id:guid}", GetDownloadClientById)
            .WithName("GetDownloadClientById")
            .WithTags("DownloadClients")
            .Produces<DownloadClient>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapPost("", CreateDownloadClient)
            .WithName("CreateDownloadClient")
            .WithTags("DownloadClients")
            .Produces<DownloadClient>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapPatch("/{id:guid}", PatchDownloadClient)
            .WithName("PatchDownloadClient")
            .WithTags("DownloadClients")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapDelete("/{id:guid}", DeleteDownloadClient)
            .WithName("DeleteDownloadClient")
            .WithTags("DownloadClients")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();
    }

    private async Task<IResult> GetAllDownloadClients(CancellationToken cancellationToken)
    {
        var downloadClients = await downloadClientRepository.ListAllAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(downloadClients);
    }

    private async Task<IResult> GetDownloadClientById(Guid id, CancellationToken cancellationToken)
    {
        var downloadClient = await downloadClientRepository.GetByIdAsync(id, cancellationToken)
            .ConfigureAwait(false);

        return downloadClient is null
            ? Results.NotFound()
            : Results.Ok(downloadClient);
    }

    private async Task<IResult> CreateDownloadClient(
        HttpContext httpContext,
        DownloadClient request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var downloadClient = request with
        {
            Id = Guid.NewGuid(),
            Status = DownloadClientStatus.Unknown,
            LastChecked = null,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = currentUserId,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        var created = await downloadClientRepository.CreateAsync(downloadClient, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"/api/download-clients/{created.Id}", created);
    }

    private async Task<IResult> PatchDownloadClient(
        HttpContext httpContext,
        Guid id,
        DownloadClientPatch request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var patch = request with { Id = id };

        var updated = await downloadClientRepository.PatchAsync(patch, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return updated
            ? Results.NoContent()
            : Results.NotFound();
    }

    private async Task<IResult> DeleteDownloadClient(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var deleted = await downloadClientRepository.DeleteAsync(id, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return deleted
            ? Results.NoContent()
            : Results.NotFound();
    }
}
