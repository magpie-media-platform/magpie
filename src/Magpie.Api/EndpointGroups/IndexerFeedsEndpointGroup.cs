using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class IndexerFeedsEndpointGroup : IApiEndpointGroup
{
    public string Name => "indexer-feeds";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetIndexerFeedsAsync)
            .WithName(nameof(GetIndexerFeedsAsync))
            .Produces<IReadOnlyList<IndexerFeedResponse>>()
            .RequireViewer();

        builder.MapPost("", CreateIndexerFeedAsync)
            .WithName(nameof(CreateIndexerFeedAsync))
            .Produces<IndexerFeedResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAdmin();

        builder.MapPost("test", TestIndexerFeedSettingsAsync)
            .WithName(nameof(TestIndexerFeedSettingsAsync))
            .Produces<ConfigurationTestResult>()
            .RequireAdmin();

        builder.MapPatch("{id:guid}", PatchIndexerFeedAsync)
            .WithName(nameof(PatchIndexerFeedAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapDelete("{id:guid}", DeleteIndexerFeedAsync)
            .WithName(nameof(DeleteIndexerFeedAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapPost("{id:guid}/test", TestIndexerFeedAsync)
            .WithName(nameof(TestIndexerFeedAsync))
            .Produces<ConfigurationTestResult>()
            .RequireAdmin();
    }

    internal async Task<IResult> GetIndexerFeedsAsync(
        [FromServices] IIndexerService indexerService,
        CancellationToken cancellationToken)
    {
        var feeds = await indexerService.ListFeedsAsync(cancellationToken).ConfigureAwait(false);

        var responses = new List<IndexerFeedResponse>(feeds.Count);
        foreach (var feed in feeds)
        {
            var schema = await indexerService.GetFeedSettingsSchemaAsync(feed.ProviderType, cancellationToken)
                .ConfigureAwait(false);
            responses.Add(CreateResponse(feed, schema));
        }

        return Results.Ok(responses);
    }

    internal async Task<IResult> CreateIndexerFeedAsync(
        [FromServices] IIndexerService indexerService,
        AuthenticatedUser user,
        IndexerFeed feed,
        CancellationToken cancellationToken)
    {
        var created = await indexerService.CreateFeedAsync(feed, user.Id, cancellationToken).ConfigureAwait(false);
        var schema = await indexerService.GetFeedSettingsSchemaAsync(created.ProviderType, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created(
            $"/api/{Name}/{created.Id}",
            CreateResponse(created, schema));
    }

    internal async Task<IResult> TestIndexerFeedSettingsAsync(
        [FromServices] IIndexerService indexerService,
        IntegrationSettingsTestRequest request,
        CancellationToken cancellationToken)
    {
        var result = await indexerService.TestFeedSettingsAsync(
            request.ProviderType,
            request.Id,
            request.Settings ?? new Dictionary<string, string>(),
            cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    internal async Task<IResult> PatchIndexerFeedAsync(
        [FromServices] IIndexerService indexerService,
        AuthenticatedUser user,
        Guid id,
        IndexerFeedPatch patch,
        CancellationToken cancellationToken)
    {
        var patched = await indexerService.PatchFeedAsync(id, patch, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return patched ? Results.NoContent() : Results.NotFound();
    }

    internal async Task<IResult> DeleteIndexerFeedAsync(
        [FromServices] IIndexerService indexerService,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        var deleted = await indexerService.DeleteFeedAsync(id, user.Id, cancellationToken).ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }

    internal async Task<IResult> TestIndexerFeedAsync(
        [FromServices] IIndexerService indexerService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await indexerService.TestFeedAsync(id, cancellationToken).ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static IndexerFeedResponse CreateResponse(IndexerFeed feed, IReadOnlyList<SettingSchemaDefinition> schema)
    {
        return new()
        {
            Id = feed.Id,
            ExtensionId = feed.ExtensionId,
            ProviderType = feed.ProviderType,
            Name = feed.Name,
            Description = feed.Description,
            Enabled = feed.Enabled,
            LastSync = feed.LastSync,
            Settings = feed.Settings.RedactSecrets(schema),
            CreatedAt = feed.CreatedAt,
            CreatedByUserId = feed.CreatedByUserId,
            ModifiedAt = feed.ModifiedAt,
            ModifiedByUserId = feed.ModifiedByUserId,
        };
    }
}

