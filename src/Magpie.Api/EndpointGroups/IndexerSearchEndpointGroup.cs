using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Api;
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

        builder.MapPost("{contentHierarchyItemId:guid}/match/{releaseId:guid}", MatchAsync)
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
        Guid releaseId,
        CancellationToken cancellationToken)
    {
        var matchedReleaseId = await indexerService
            .MatchAsync(contentHierarchyItemId, releaseId, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return matchedReleaseId is { } id ? Results.Ok(id) : Results.NotFound();
    }
}
