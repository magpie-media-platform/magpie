using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Api;
using Magpie.Models.Data;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class IndexerSearchEndpointGroup : IApiEndpointGroup
{
    public string Name => "indexer-search";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapPost("", SearchIndexersAsync)
            .WithName(nameof(SearchIndexersAsync))
            .Produces<IndexerSearchResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAdmin();

        builder.MapPost("{contentHierarchyItemId:guid}/match", MatchAsync)
            .WithName(nameof(MatchAsync))
            .Produces<Guid>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();
    }

    internal async Task<IResult> SearchIndexersAsync(
        [FromServices] IIndexerService indexerService,
        IndexerSearchRequest request,
        CancellationToken cancellationToken)
    {
        var result = await indexerService.SearchAsync(request, cancellationToken).ConfigureAwait(false);

        return result is null
            ? Results.Problem(
                "The selected indexers share no query mode that fits the request.",
                statusCode: StatusCodes.Status400BadRequest)
            : Results.Ok(result);
    }

    internal async Task<IResult> MatchAsync(
        [FromServices] IIndexerService indexerService,
        AuthenticatedUser user,
        Guid contentHierarchyItemId,
        Release request,
        CancellationToken cancellationToken)
    {
        var releaseId = await indexerService
            .MatchAsync(contentHierarchyItemId, request, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return releaseId is { } id ? Results.Ok(id) : Results.NotFound();
    }
}
