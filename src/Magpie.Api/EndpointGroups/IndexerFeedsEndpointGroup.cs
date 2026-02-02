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

        builder.MapPost("", CreateIndexerFeed)
            .WithName("CreateIndexerFeed")
            .WithTags("IndexerFeeds")
            .Produces<IndexerFeed>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapPut("/{id:guid}", UpdateIndexerFeed)
            .WithName("UpdateIndexerFeed")
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

    private async Task<IResult> GetAllIndexerFeeds(CancellationToken cancellationToken)
    {
        var feeds = await indexerFeedRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(feeds);
    }

    private async Task<IResult> GetIndexerFeedById(Guid id, CancellationToken cancellationToken)
    {
        var feed = await indexerFeedRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return feed is null ? Results.NotFound() : Results.Ok(feed);
    }

    private async Task<IResult> CreateIndexerFeed(
        HttpContext httpContext,
        IndexerFeed request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var feed = request with
        {
            Id = Guid.NewGuid(),
            LastSync = null,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = currentUserId,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        var created = await indexerFeedRepository.CreateAsync(feed, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"/api/indexer-feeds/{created.Id}", created);
    }

    private async Task<IResult> UpdateIndexerFeed(
        HttpContext httpContext,
        Guid id,
        IndexerFeed request,
        CancellationToken cancellationToken)
    {
        var existing = await indexerFeedRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            return Results.NotFound();
        }

        var currentUserId = httpContext.User.GetUserId();

        var feed = request with
        {
            Id = id,
            LastSync = existing.LastSync,
            CreatedAt = existing.CreatedAt,
            CreatedByUserId = existing.CreatedByUserId,
            ModifiedAt = DateTime.UtcNow,
            ModifiedByUserId = currentUserId
        };

        await indexerFeedRepository.UpdateAsync(feed, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.NoContent();
    }

    private async Task<IResult> DeleteIndexerFeed(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var deleted = await indexerFeedRepository.DeleteAsync(id, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
