using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Api;
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
    private const string NoDownloadClientProblemType = "urn:magpie:problem:no-download-client";
    private const string AcquisitionInProgressProblemType = "urn:magpie:problem:acquisition-in-progress";

    public string Name => "interactive-search";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapPost("{contentHierarchyItemId:guid}", SearchAsync)
            .WithName(nameof(SearchAsync))
            .Produces<InteractiveSearchResult>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRequester();

        builder.MapPost("{contentHierarchyItemId:guid}/grab/{releaseId:guid}", GrabAsync)
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
            .SearchInteractiveResultAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);

        return candidates is null ? Results.NotFound() : Results.Ok(candidates);
    }

    internal async Task<IResult> GrabAsync(
        [FromServices] IReleaseSearchService releaseSearchService,
        AuthenticatedUser user,
        Guid contentHierarchyItemId,
        Guid releaseId,
        CancellationToken cancellationToken)
    {
        var result = await releaseSearchService
            .GrabInteractiveAsync(contentHierarchyItemId, releaseId, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return result.Status switch
        {
            InteractiveReleaseGrabResult.Accepted =>
                Results.Accepted($"/api/{Name}/{contentHierarchyItemId}/grab/{releaseId}", result.ExecutionId),
            InteractiveReleaseGrabResult.ContentNotFound => Results.NotFound(),
            InteractiveReleaseGrabResult.NoDownloadClient => Results.Problem(
                detail: result.Detail,
                statusCode: StatusCodes.Status409Conflict,
                type: NoDownloadClientProblemType),
            InteractiveReleaseGrabResult.AlreadyInProgress => Results.Problem(
                detail: result.Detail,
                statusCode: StatusCodes.Status409Conflict,
                type: AcquisitionInProgressProblemType),
            _ => Results.Problem(
                detail: result.Detail,
                statusCode: StatusCodes.Status400BadRequest),
        };
    }
}
