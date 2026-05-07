using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Integrations;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class IndexersEndpointGroup(
    IIndexerRepository indexerRepository,
    IIndexerConnectionRepository indexerConnectionRepository,
    IEnumerable<IIndexerIntegration> indexerIntegrations) : IApiEndpointGroup
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

        builder.MapPost("{id:guid}/test", TestIndexer)
            .WithName(nameof(TestIndexer))
            .Produces<ConnectionTestResult>()
            .ProducesProblem(StatusCodes.Status404NotFound)
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
        IndexerPatch request,
        CancellationToken cancellationToken)
    {
        var existing = await indexerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            return Results.NotFound();
        }

        var currentUserId = httpContext.User.GetUserId();

        var indexer = existing with
        {
            ProviderType = request.ProviderType ?? existing.ProviderType,
            Name = request.Name ?? existing.Name,
            Description = request.Description ?? existing.Description,
            Availability = request.Availability ?? existing.Availability,
            Categories = request.Categories ?? existing.Categories,
            Languages = request.Languages ?? existing.Languages,
            ModifiedAt = DateTime.UtcNow,
            ModifiedByUserId = currentUserId
        };

        await indexerRepository.UpdateAsync(indexer, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.NoContent();
    }

    private async Task<IResult> TestIndexer(
        Guid id,
        Guid? connectionId,
        CancellationToken cancellationToken)
    {
        var indexer = await indexerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (indexer is null)
        {
            return Results.NotFound();
        }

        var integration = indexerIntegrations
            .FirstOrDefault(i => string.Equals(
                i.IndexerType,
                indexer.ProviderType,
                StringComparison.OrdinalIgnoreCase));

        if (integration is null)
        {
            return Results.Ok(new ConnectionTestResult
            {
                Success = false,
                Message = $"No integration registered for type '{indexer.ProviderType}'."
            });
        }

        // Indexer-level settings are stored on IndexerConnection rather than on the Indexer entity
        // itself. Resolve the connection whose settings should be used: either the explicit
        // connectionId (if supplied), or the indexer's sole connection. Otherwise fail.
        var connections = (await indexerConnectionRepository
            .ListByIndexerIdAsync(id, cancellationToken)
            .ConfigureAwait(false)).ToList();

        IndexerConnection? connection;
        if (connectionId is { } cid)
        {
            connection = connections.FirstOrDefault(c => c.Id == cid);
            if (connection is null)
            {
                return Results.Ok(new ConnectionTestResult
                {
                    Success = false,
                    Message = $"Connection '{cid}' was not found for this indexer."
                });
            }
        }
        else if (connections.Count == 0)
        {
            return Results.Ok(new ConnectionTestResult
            {
                Success = false,
                Message = "No connections are configured for this indexer."
            });
        }
        else if (connections.Count > 1)
        {
            return Results.Ok(new ConnectionTestResult
            {
                Success = false,
                Message = "Multiple connections are configured; specify connectionId to choose one."
            });
        }
        else
        {
            connection = connections[0];
        }

        IReadOnlyDictionary<string, string> settings = connection!.Settings;

        var result = await integration.TestConnectionAsync(settings, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
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
