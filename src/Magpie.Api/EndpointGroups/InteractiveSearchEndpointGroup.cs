using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Api;
using Magpie.Models.Data;
using Magpie.Sdk.Api;
using Magpie.Sdk.Releases;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class InteractiveSearchEndpointGroup : IApiEndpointGroup
{
    public string Name => "interactive-search";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapPost("{contentHierarchyItemId:guid}", SearchAsync)
            .WithName(nameof(SearchAsync))
            .Produces<IReadOnlyList<InteractiveSearchCandidate>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRequester();

        builder.MapPost("{contentHierarchyItemId:guid}/grab", GrabAsync)
            .WithName(nameof(GrabAsync))
            .Produces<Guid>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRequester();
    }

    internal async Task<IResult> SearchAsync(
        [FromServices] IReleaseSearchService releaseSearchService,
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken)
    {
        var candidates = await releaseSearchService
            .SearchInteractiveAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);

        return candidates is null ? Results.NotFound() : Results.Ok(candidates);
    }

    internal async Task<IResult> GrabAsync(
        [FromServices] IReleaseSearchService releaseSearchService,
        AuthenticatedUser user,
        Guid contentHierarchyItemId,
        Release request,
        CancellationToken cancellationToken)
    {
        var result = await releaseSearchService
            .GrabInteractiveAsync(contentHierarchyItemId, request, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return result.Status switch
        {
            InteractiveReleaseGrabResult.Accepted =>
                Results.Accepted($"api/{Name}/{contentHierarchyItemId}/grab", result.ExecutionId),
            InteractiveReleaseGrabResult.ContentNotFound => Results.NotFound(),
            InteractiveReleaseGrabResult.NoDownloadClient => Results.Problem(
                detail: result.Detail,
                statusCode: StatusCodes.Status409Conflict),
            _ => Results.Problem(
                detail: result.Detail,
                statusCode: StatusCodes.Status400BadRequest),
        };
    }
}
