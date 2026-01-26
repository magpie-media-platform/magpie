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

internal sealed class IndexerFeedsEndpointGroup(IIndexerFeedRepository indexerFeedRepository) : IApiEndpointGroup
{
    public string Name => "indexer-feeds";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetAllIndexerFeeds)
            .WithName("GetAllIndexerFeeds")
            .WithTags("IndexerFeeds")
            .Produces<IndexerFeed[]>()
            .RequireAuthorization();

        builder.MapGet("/{id:guid}", GetIndexerFeedById)
            .WithName("GetIndexerFeedById")
            .WithTags("IndexerFeeds")
            .Produces<IndexerFeed>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapGet("/by-name/{name}", GetIndexerFeedByName)
            .WithName("GetIndexerFeedByName")
            .WithTags("IndexerFeeds")
            .Produces<IndexerFeed>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapGet("/by-provider/{provider}", GetIndexerFeedsByProvider)
            .WithName("GetIndexerFeedsByProvider")
            .WithTags("IndexerFeeds")
            .Produces<IndexerFeed[]>()
            .RequireAuthorization();

        builder.MapGet("/enabled", GetEnabledIndexerFeeds)
            .WithName("GetEnabledIndexerFeeds")
            .WithTags("IndexerFeeds")
            .Produces<IndexerFeed[]>()
            .RequireAuthorization();

        builder.MapPost("", CreateIndexerFeed)
            .WithName("CreateIndexerFeed")
            .WithTags("IndexerFeeds")
            .Produces<IndexerFeed>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapPatch("/{id:guid}", PatchIndexerFeed)
            .WithName("PatchIndexerFeed")
            .WithTags("IndexerFeeds")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapDelete("/{id:guid}", DeleteIndexerFeed)
            .WithName("DeleteIndexerFeed")
            .WithTags("IndexerFeeds")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();
    }

    private async Task<HttpResult> GetAllIndexerFeeds(CancellationToken cancellationToken)
    {
        var result = await indexerFeedRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<IndexerFeed>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<IndexerFeed>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetIndexerFeedById(Guid id, CancellationToken cancellationToken)
    {
        var result = await indexerFeedRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IndexerFeed?> { Value: null } => Results.NotFound(),
            SuccessResult<IndexerFeed?> success => Results.Ok(success.Value),
            FailureResult<IndexerFeed?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetIndexerFeedByName(string name, CancellationToken cancellationToken)
    {
        var result = await indexerFeedRepository.GetByNameAsync(name, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IndexerFeed?> { Value: null } => Results.NotFound(),
            SuccessResult<IndexerFeed?> success => Results.Ok(success.Value),
            FailureResult<IndexerFeed?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetIndexerFeedsByProvider(string provider, CancellationToken cancellationToken)
    {
        var result = await indexerFeedRepository.GetByProviderAsync(provider, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<IndexerFeed>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<IndexerFeed>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetEnabledIndexerFeeds(CancellationToken cancellationToken)
    {
        var result = await indexerFeedRepository.GetEnabledAsync(cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<IndexerFeed>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<IndexerFeed>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> CreateIndexerFeed(IndexerFeed request, CancellationToken cancellationToken)
    {
        var indexerFeed = request with
        {
            Id = Guid.NewGuid(),
            LastSync = null,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        var result = await indexerFeedRepository.CreateAsync(indexerFeed, createdByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IndexerFeed> success =>
                Results.Created($"/api/indexer-feeds/{success.Value.Id}", success.Value),
            FailureResult<IndexerFeed> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status400BadRequest),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> PatchIndexerFeed(
        Guid id,
        IndexerFeedPatch request,
        CancellationToken cancellationToken)
    {
        var patch = request with { Id = id };

        var result = await indexerFeedRepository.PatchAsync(patch, modifiedByUserId: null, cancellationToken)
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

    private async Task<HttpResult> DeleteIndexerFeed(Guid id, CancellationToken cancellationToken)
    {
        var result = await indexerFeedRepository.DeleteAsync(id, deletedByUserId: null, cancellationToken)
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
