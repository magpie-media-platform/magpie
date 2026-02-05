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

internal sealed class IndexersEndpointGroup(IIndexerRepository indexerRepository) : IApiEndpointGroup
{
    public string Name => "indexers";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetAllIndexers)
            .WithName(nameof(GetAllIndexers))
            .Produces<Indexer[]>()
            .RequireViewer();

        builder.MapGet("{id:guid}", GetIndexerById)
            .WithName(nameof(GetIndexerById))
            .Produces<Indexer>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPost("", CreateIndexer)
            .WithName(nameof(CreateIndexer))
            .Produces<Indexer>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapPatch("{id:guid}", PatchIndexer)
            .WithName(nameof(PatchIndexer))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapDelete("{id:guid}", DeleteIndexer)
            .WithName(nameof(DeleteIndexer))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();
    }

    private async Task<IResult> GetAllIndexers(CancellationToken cancellationToken)
    {
        var indexers = await indexerRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(indexers);
    }

    private async Task<IResult> GetIndexerById(Guid id, CancellationToken cancellationToken)
    {
        var indexer = await indexerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return indexer is null ? Results.NotFound() : Results.Ok(indexer);
    }

    private async Task<IResult> CreateIndexer(
        HttpContext httpContext,
        Indexer request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var indexer = request with
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = currentUserId,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        var created = await indexerRepository.CreateAsync(indexer, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"api/{Name}/{created.Id}", created);
    }

    private async Task<IResult> PatchIndexer(
        HttpContext httpContext,
        Guid id,
        Indexer request,
        CancellationToken cancellationToken)
    {
        var existing = await indexerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            return Results.NotFound();
        }

        var currentUserId = httpContext.User.GetUserId();

        var indexer = request with
        {
            Id = id,
            CreatedAt = existing.CreatedAt,
            CreatedByUserId = existing.CreatedByUserId,
            ModifiedAt = DateTime.UtcNow,
            ModifiedByUserId = currentUserId
        };

        await indexerRepository.UpdateAsync(indexer, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.NoContent();
    }

    private async Task<IResult> DeleteIndexer(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var deleted = await indexerRepository.DeleteAsync(id, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
