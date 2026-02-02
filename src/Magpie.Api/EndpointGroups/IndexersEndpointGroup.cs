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
            .WithName("GetAllIndexers")
            .WithTags("Indexers")
            .Produces<Indexer[]>()
            .RequireAuthorization();

        builder.MapGet("/{id:guid}", GetIndexerById)
            .WithName("GetIndexerById")
            .WithTags("Indexers")
            .Produces<Indexer>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapPost("", CreateIndexer)
            .WithName("CreateIndexer")
            .WithTags("Indexers")
            .Produces<Indexer>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapPut("/{id:guid}", UpdateIndexer)
            .WithName("UpdateIndexer")
            .WithTags("Indexers")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapDelete("/{id:guid}", DeleteIndexer)
            .WithName("DeleteIndexer")
            .WithTags("Indexers")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();
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

        return Results.Created($"/api/indexers/{created.Id}", created);
    }

    private async Task<IResult> UpdateIndexer(
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
