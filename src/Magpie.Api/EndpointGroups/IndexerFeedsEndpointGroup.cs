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
            .WithName(nameof(GetAllIndexerFeeds))
            .Produces<IndexerFeed[]>()
            .RequireViewer();

        builder.MapGet("{id:guid}", GetIndexerFeedById)
            .WithName(nameof(GetIndexerFeedById))
            .Produces<IndexerFeed>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPost("", CreateIndexerFeed)
            .WithName(nameof(CreateIndexerFeed))
            .Produces<IndexerFeed>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapPatch("{id:guid}", PatchIndexerFeed)
            .WithName(nameof(PatchIndexerFeed))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapPost("{id:guid}/enable", EnableIndexerFeed)
            .WithName(nameof(EnableIndexerFeed))
            .Produces<IndexerFeed>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapPost("{id:guid}/disable", DisableIndexerFeed)
            .WithName(nameof(DisableIndexerFeed))
            .Produces<IndexerFeed>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapDelete("{id:guid}", DeleteIndexerFeed)
            .WithName(nameof(DeleteIndexerFeed))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();
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

        return Results.Created($"api/{Name}/{created.Id}", created);
    }

    private async Task<IResult> PatchIndexerFeed(
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

    private async Task<IResult> EnableIndexerFeed(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var existing = await indexerFeedRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            return Results.NotFound();
        }

        var currentUserId = httpContext.User.GetUserId();

        var feed = existing with
        {
            Enabled = true,
            ModifiedAt = DateTime.UtcNow,
            ModifiedByUserId = currentUserId
        };

        await indexerFeedRepository.UpdateAsync(feed, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(feed);
    }

    private async Task<IResult> DisableIndexerFeed(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var existing = await indexerFeedRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            return Results.NotFound();
        }

        var currentUserId = httpContext.User.GetUserId();

        var feed = existing with
        {
            Enabled = false,
            ModifiedAt = DateTime.UtcNow,
            ModifiedByUserId = currentUserId
        };

        await indexerFeedRepository.UpdateAsync(feed, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(feed);
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
