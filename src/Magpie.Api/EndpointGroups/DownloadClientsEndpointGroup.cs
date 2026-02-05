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
            .WithName(nameof(GetAllDownloadClients))
            .Produces<DownloadClient[]>()
            .RequireViewer();

        builder.MapGet("{id:guid}", GetDownloadClientById)
            .WithName(nameof(GetDownloadClientById))
            .Produces<DownloadClient>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPost("", CreateDownloadClient)
            .WithName(nameof(CreateDownloadClient))
            .Produces<DownloadClient>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapPatch("{id:guid}", PatchDownloadClient)
            .WithName(nameof(PatchDownloadClient))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapPost("{id:guid}/enable", EnableDownloadClient)
            .WithName(nameof(EnableDownloadClient))
            .Produces<DownloadClient>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapPost("{id:guid}/disable", DisableDownloadClient)
            .WithName(nameof(DisableDownloadClient))
            .Produces<DownloadClient>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapDelete("{id:guid}", DeleteDownloadClient)
            .WithName(nameof(DeleteDownloadClient))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();
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

        return Results.Created($"api/{Name}/{created.Id}", created);
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

    private async Task<IResult> EnableDownloadClient(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var existing = await downloadClientRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            return Results.NotFound();
        }

        var currentUserId = httpContext.User.GetUserId();

        var downloadClient = existing with
        {
            Enabled = true,
            ModifiedAt = DateTime.UtcNow,
            ModifiedByUserId = currentUserId
        };

        await downloadClientRepository.UpdateAsync(downloadClient, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(downloadClient);
    }

    private async Task<IResult> DisableDownloadClient(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var existing = await downloadClientRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            return Results.NotFound();
        }

        var currentUserId = httpContext.User.GetUserId();

        var downloadClient = existing with
        {
            Enabled = false,
            ModifiedAt = DateTime.UtcNow,
            ModifiedByUserId = currentUserId
        };

        await downloadClientRepository.UpdateAsync(downloadClient, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(downloadClient);
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
