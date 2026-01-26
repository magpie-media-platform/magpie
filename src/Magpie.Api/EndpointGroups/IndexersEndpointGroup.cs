using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Data;
using Magpie.Models.Indexers;
using Magpie.Models.Result;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using HttpResult = Microsoft.AspNetCore.Http.IResult;

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

        builder.MapGet("/by-name/{name}", GetIndexerByName)
            .WithName("GetIndexerByName")
            .WithTags("Indexers")
            .Produces<Indexer>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapGet("/by-definition/{definitionId:guid}", GetIndexersByDefinition)
            .WithName("GetIndexersByDefinition")
            .WithTags("Indexers")
            .Produces<Indexer[]>()
            .RequireAuthorization();

        builder.MapGet("/by-status/{status}", GetIndexersByStatus)
            .WithName("GetIndexersByStatus")
            .WithTags("Indexers")
            .Produces<Indexer[]>()
            .RequireAuthorization();

        builder.MapGet("/enabled", GetEnabledIndexers)
            .WithName("GetEnabledIndexers")
            .WithTags("Indexers")
            .Produces<Indexer[]>()
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

        builder.MapPatch("/{id:guid}", PatchIndexer)
            .WithName("PatchIndexer")
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

    private async Task<HttpResult> GetAllIndexers(CancellationToken cancellationToken)
    {
        var result = await indexerRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Indexer>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Indexer>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetIndexerById(Guid id, CancellationToken cancellationToken)
    {
        var result = await indexerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<Indexer?> { Value: null } => Results.NotFound(),
            SuccessResult<Indexer?> success => Results.Ok(success.Value),
            FailureResult<Indexer?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetIndexerByName(string name, CancellationToken cancellationToken)
    {
        var result = await indexerRepository.GetByNameAsync(name, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<Indexer?> { Value: null } => Results.NotFound(),
            SuccessResult<Indexer?> success => Results.Ok(success.Value),
            FailureResult<Indexer?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetIndexersByDefinition(Guid definitionId, CancellationToken cancellationToken)
    {
        var result = await indexerRepository.GetByDefinitionIdAsync(definitionId, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Indexer>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Indexer>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetIndexersByStatus(IndexerStatus status, CancellationToken cancellationToken)
    {
        var result = await indexerRepository.GetByStatusAsync(status, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Indexer>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Indexer>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetEnabledIndexers(CancellationToken cancellationToken)
    {
        var result = await indexerRepository.GetEnabledAsync(cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Indexer>> success =>
                Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Indexer>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ =>
                Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> CreateIndexer(Indexer request, CancellationToken cancellationToken)
    {
        var indexer = request with
        {
            Id = Guid.NewGuid(),
            Status = IndexerStatus.Unknown,
            LastChecked = null,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        var result = await indexerRepository.CreateAsync(indexer, createdByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<Indexer> success => Results.Created($"/api/indexers/{success.Value.Id}", success.Value),
            FailureResult<Indexer> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status400BadRequest),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> UpdateIndexer(Guid id, Indexer request, CancellationToken cancellationToken)
    {
        var existingResult = await indexerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (existingResult is SuccessResult<Indexer?> { Value: null })
        {
            return Results.NotFound();
        }

        if (existingResult is FailureResult<Indexer?> existingFailure)
        {
            return Results.Problem(existingFailure.Reason, statusCode: StatusCodes.Status500InternalServerError);
        }

        if (existingResult is not SuccessResult<Indexer?> { Value: { } existing })
        {
            return Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError);
        }

        var indexer = request with
        {
            Id = id,
            Status = existing.Status,
            LastChecked = existing.LastChecked,
            CreatedAt = existing.CreatedAt,
            CreatedByUserId = existing.CreatedByUserId,
            ModifiedAt = DateTimeOffset.UtcNow,
            ModifiedByUserId = null
        };

        var result = await indexerRepository.UpdateAsync(indexer, modifiedByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<bool> => Results.NoContent(),
            FailureResult<bool> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status400BadRequest),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> PatchIndexer(Guid id, IndexerPatch request, CancellationToken cancellationToken)
    {
        var patch = request with { Id = id };

        var result = await indexerRepository.PatchAsync(patch, modifiedByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<bool> { Value: false } => Results.NotFound(),
            SuccessResult<bool> => Results.NoContent(),
            FailureResult<bool> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status400BadRequest),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> DeleteIndexer(Guid id, CancellationToken cancellationToken)
    {
        var result = await indexerRepository.DeleteAsync(id, deletedByUserId: null, cancellationToken)
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
