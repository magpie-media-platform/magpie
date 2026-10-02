using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.MetadataSearch;
using Magpie.Sdk.Api;
using Magpie.Sdk.Content;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

[MagpieCommandGroup("content-search", "content-search", "Search external providers for content")]
internal sealed class ContentSearchEndpointGroup : IApiEndpointGroup
{
    public string Name => "content-search";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", SearchProvidersAsync)
            .WithName(nameof(SearchProvidersAsync))
            .Produces<ProviderMetadataCandidateSearchResult>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireRequester();
    }

    [MagpieCommand("providers", "GET", "", "Search external providers for content")]
    internal async Task<IResult> SearchProvidersAsync(
        [FromServices] IFilterService filterService,
        [FromServices] IMetadataSearchService metadataSearchService,
        string? query,
        string? contentType,
        int? page,
        CancellationToken cancellationToken,
        string? continuation = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Results.Problem(detail: "A query is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var parseResult = filterService.ParseContentCriteria(query);
        if (parseResult.Error is { } error)
        {
            return Results.Problem(
                detail: error.Message,
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?>
                {
                    ["position"] = error.Position,
                    ["expected"] = error.Expected,
                });
        }

        var criteria = parseResult.Value
            ?? throw new InvalidOperationException("The parsed query has no criteria.");

        try
        {
            var result = await metadataSearchService
                .SearchCandidatesAsync(criteria, contentType, page ?? 1, continuation, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(result);
        }
        catch (MetadataSearchContinuationException exception)
        {
            return Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_search_continuation" });
        }
    }
}

