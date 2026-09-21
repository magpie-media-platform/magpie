using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.MetadataSearch;
using Magpie.Sdk.Api;
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
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Results.Problem(
                detail: "A query is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var parseResult = filterService.ParseContentCriteria(query);
        if (!parseResult.IsSuccess)
        {
            var error = parseResult.Error!;
            return Results.Problem(
                detail: error.Message,
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?>
                {
                    ["position"] = error.Position,
                    ["expected"] = error.Expected,
                });
        }

        var result = await metadataSearchService
            .SearchCandidatesAsync(parseResult.Value!, contentType, page ?? 1, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }
}

