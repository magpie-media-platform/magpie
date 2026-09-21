using System;
using System.Collections.Generic;
using System.Linq;
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

[MagpieCommandGroup("indexers", "indexers", "Manage indexers")]
internal sealed class IndexersEndpointGroup : IApiEndpointGroup
{
    public string Name => "indexers";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetIndexersAsync)
            .WithName(nameof(GetIndexersAsync))
            .Produces<IReadOnlyList<IndexerResponse>>()
            .RequireViewer();

        builder.MapPost("", CreateIndexerAsync)
            .WithName(nameof(CreateIndexerAsync))
            .Produces<IndexerResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAdmin();

        builder.MapPost("test", TestIndexerSettingsAsync)
            .WithName(nameof(TestIndexerSettingsAsync))
            .Produces<ConfigurationTestResult>()
            .RequireAdmin();

        builder.MapGet("{id:guid}", GetIndexerAsync)
            .WithName(nameof(GetIndexerAsync))
            .Produces<IndexerResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPatch("{id:guid}", PatchIndexerAsync)
            .WithName(nameof(PatchIndexerAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapDelete("{id:guid}", DeleteIndexerAsync)
            .WithName(nameof(DeleteIndexerAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapGet("{id:guid}/schema", GetIndexerSchemaAsync)
            .WithName(nameof(GetIndexerSchemaAsync))
            .Produces<IReadOnlyList<SettingSchemaDefinition>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPost("{id:guid}/test", TestIndexerAsync)
            .WithName(nameof(TestIndexerAsync))
            .Produces<ConfigurationTestResult>()
            .RequireAdmin();

        builder.MapGet("{id:guid}/connections", GetIndexerConnectionsAsync)
            .WithName(nameof(GetIndexerConnectionsAsync))
            .Produces<IReadOnlyList<IndexerConnection>>()
            .RequireViewer();

        builder.MapPost("{id:guid}/connections", CreateIndexerConnectionAsync)
            .WithName(nameof(CreateIndexerConnectionAsync))
            .Produces<IndexerConnection>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapPatch("{id:guid}/connections/{connectionId:guid}", PatchIndexerConnectionAsync)
            .WithName(nameof(PatchIndexerConnectionAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapDelete("{id:guid}/connections/{connectionId:guid}", DeleteIndexerConnectionAsync)
            .WithName(nameof(DeleteIndexerConnectionAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapPost("{id:guid}/connections/{connectionId:guid}/test", TestIndexerConnectionAsync)
            .WithName(nameof(TestIndexerConnectionAsync))
            .Produces<ConfigurationTestResult>()
            .RequireAdmin();
    }

    [MagpieCommand("list", "GET", "", "List indexers")]
    internal async Task<IResult> GetIndexersAsync(
        [FromServices] IIndexerService indexerService,
        CancellationToken cancellationToken)
    {
        var indexers = await indexerService.ListAsync(cancellationToken).ConfigureAwait(false);
        var capabilities = (await indexerService.ListCapabilitiesAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(capability => capability.IndexerId);

        var responses = new List<IndexerResponse>(indexers.Count);
        foreach (var indexer in indexers)
        {
            var connections = await indexerService.ListConnectionsAsync(indexer.Id, cancellationToken)
                .ConfigureAwait(false);
            capabilities.TryGetValue(indexer.Id, out var indexerCapabilities);
            responses.Add(CreateResponse(indexer, connections.Count, indexerCapabilities));
        }

        return Results.Ok(responses);
    }

    [MagpieCommand("create", "POST", "", "Create an indexer")]
    internal async Task<IResult> CreateIndexerAsync(
        [FromServices] IIndexerService indexerService,
        AuthenticatedUser user,
        [FromBody] IndexerCreate create,
        CancellationToken cancellationToken)
    {
        var created = await indexerService.CreateAsync(create, user.Id, cancellationToken).ConfigureAwait(false);

        var connections = await indexerService.ListConnectionsAsync(created.Id, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"/api/{Name}/{created.Id}", CreateResponse(created, connections.Count));
    }

    internal async Task<IResult> TestIndexerSettingsAsync(
        [FromServices] IIndexerService indexerService,
        IntegrationSettingsTestRequest request,
        CancellationToken cancellationToken)
    {
        var result = await indexerService.TestSettingsAsync(
            request.ProviderType,
            request.Id,
            request.Settings ?? new Dictionary<string, string>(),
            cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    internal async Task<IResult> GetIndexerAsync(
        [FromServices] IIndexerService indexerService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var indexer = await indexerService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (indexer is null)
        {
            return Results.NotFound();
        }

        var connections = await indexerService.ListConnectionsAsync(id, cancellationToken).ConfigureAwait(false);

        return Results.Ok(CreateResponse(indexer, connections.Count));
    }

    [MagpieCommand("patch", "PATCH", "{id:guid}", "Update fields on an indexer")]
    internal async Task<IResult> PatchIndexerAsync(
        [FromServices] IIndexerService indexerService,
        AuthenticatedUser user,
        Guid id,
        [FromBody] IndexerPatch patch,
        CancellationToken cancellationToken)
    {
        var patched = await indexerService.PatchAsync(id, patch, user.Id, cancellationToken).ConfigureAwait(false);

        return patched ? Results.NoContent() : Results.NotFound();
    }

    internal async Task<IResult> DeleteIndexerAsync(
        [FromServices] IIndexerService indexerService,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        var deleted = await indexerService.DeleteAsync(id, user.Id, cancellationToken).ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }

    internal async Task<IResult> GetIndexerSchemaAsync(
        [FromServices] IIndexerService indexerService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var indexer = await indexerService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (indexer is null)
        {
            return Results.NotFound();
        }

        var schema = await indexerService.GetSettingsSchemaAsync(id, cancellationToken).ConfigureAwait(false);

        return Results.Ok(schema);
    }

    internal async Task<IResult> TestIndexerAsync(
        [FromServices] IIndexerService indexerService,
        Guid id,
        Guid? connectionId,
        CancellationToken cancellationToken)
    {
        var result = await indexerService.TestAsync(id, connectionId, cancellationToken).ConfigureAwait(false);

        return Results.Ok(result);
    }

    internal async Task<IResult> GetIndexerConnectionsAsync(
        [FromServices] IIndexerService indexerService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var connections = await indexerService.ListConnectionsAsync(id, cancellationToken).ConfigureAwait(false);
        var schema = await indexerService.GetSettingsSchemaAsync(id, cancellationToken).ConfigureAwait(false);

        return Results.Ok(connections.Select(connection => RedactSecrets(connection, schema)).ToList());
    }

    internal async Task<IResult> CreateIndexerConnectionAsync(
        [FromServices] IIndexerService indexerService,
        AuthenticatedUser user,
        Guid id,
        IndexerConnectionCreate create,
        CancellationToken cancellationToken)
    {
        var created = await indexerService.CreateConnectionAsync(id, create, user.Id, cancellationToken)
            .ConfigureAwait(false);
        var schema = await indexerService.GetSettingsSchemaAsync(id, cancellationToken).ConfigureAwait(false);

        return Results.Created(
            $"/api/{Name}/{id}/connections/{created.Id}",
            RedactSecrets(created, schema));
    }

    internal async Task<IResult> PatchIndexerConnectionAsync(
        [FromServices] IIndexerService indexerService,
        AuthenticatedUser user,
        Guid id,
        Guid connectionId,
        IndexerConnectionPatch patch,
        CancellationToken cancellationToken)
    {
        var patched = await indexerService.PatchConnectionAsync(id, connectionId, patch, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return patched ? Results.NoContent() : Results.NotFound();
    }

    internal async Task<IResult> DeleteIndexerConnectionAsync(
        [FromServices] IIndexerService indexerService,
        AuthenticatedUser user,
        Guid id,
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        var deleted = await indexerService.DeleteConnectionAsync(id, connectionId, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }

    internal async Task<IResult> TestIndexerConnectionAsync(
        [FromServices] IIndexerService indexerService,
        Guid id,
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        var result = await indexerService.TestConnectionAsync(
            id,
            connectionId,
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static IndexerResponse CreateResponse(
        Indexer indexer,
        int connectionCount,
        IndexerSearchCapabilities? capabilities = null)
    {
        return new()
        {
            Id = indexer.Id,
            FeedId = indexer.FeedId,
            ExtensionId = indexer.ExtensionId,
            ProviderType = indexer.ProviderType,
            Name = indexer.Name,
            Description = indexer.Description,
            Availability = indexer.Availability,
            Categories = [.. indexer.Categories],
            Languages = [.. indexer.Languages],
            SupportedContentTypes = [.. indexer.SupportedContentTypes],
            DefinitionType = indexer.DefinitionType,
            ConnectionCount = connectionCount,
            SearchFacets = capabilities?.SearchFacets ?? [],
            SupportsKeywordSearch = capabilities?.SupportsKeywordSearch ?? false,
            CreatedAt = indexer.CreatedAt,
            CreatedByUserId = indexer.CreatedByUserId,
            ModifiedAt = indexer.ModifiedAt,
            ModifiedByUserId = indexer.ModifiedByUserId,
        };
    }

    private static IndexerConnection RedactSecrets(
        IndexerConnection connection,
        IReadOnlyList<SettingSchemaDefinition> schema)
    {
        return connection with
        {
            Settings = connection.Settings.RedactSecrets(schema),
        };
    }
}
