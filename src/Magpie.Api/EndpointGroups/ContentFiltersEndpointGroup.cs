using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Data;
using Magpie.Models.Filters;
using Magpie.Models.Result;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using HttpResult = Microsoft.AspNetCore.Http.IResult;

namespace Magpie.Api.EndpointGroups;

internal sealed class ContentFiltersEndpointGroup(IContentFilterRepository contentFilterRepository) : IApiEndpointGroup
{
    public string Name => "content-filters";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/my", GetMyContentFilters)
            .WithName("GetMyContentFilters")
            .WithTags("ContentFilters")
            .Produces<ContentFilter[]>()
            .RequireAuthorization();

        builder.MapGet("", GetAllContentFilters)
            .WithName("GetAllContentFilters")
            .WithTags("ContentFilters")
            .Produces<ContentFilter[]>()
            .RequireAuthorization();

        builder.MapGet("/{id:guid}", GetContentFilterById)
            .WithName("GetContentFilterById")
            .WithTags("ContentFilters")
            .Produces<ContentFilter>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapGet("/by-name/{name}", GetContentFilterByName)
            .WithName("GetContentFilterByName")
            .WithTags("ContentFilters")
            .Produces<ContentFilter>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapGet("/by-tag/{tag}", GetContentFiltersByTag)
            .WithName("GetContentFiltersByTag")
            .WithTags("ContentFilters")
            .Produces<ContentFilter[]>()
            .RequireAuthorization();

        builder.MapPost("", CreateContentFilter)
            .WithName("CreateContentFilter")
            .WithTags("ContentFilters")
            .Produces<ContentFilter>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapPatch("/{id:guid}", PatchContentFilter)
            .WithName("PatchContentFilter")
            .WithTags("ContentFilters")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapDelete("/{id:guid}", DeleteContentFilter)
            .WithName("DeleteContentFilter")
            .WithTags("ContentFilters")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();
    }

    private async Task<HttpResult> GetMyContentFilters(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Problem("User not authenticated", statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await contentFilterRepository.GetByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<ContentFilter>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<ContentFilter>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetAllContentFilters(CancellationToken cancellationToken)
    {
        var result = await contentFilterRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<ContentFilter>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<ContentFilter>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetContentFilterById(Guid id, CancellationToken cancellationToken)
    {
        var result = await contentFilterRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<ContentFilter?> { Value: null } => Results.NotFound(),
            SuccessResult<ContentFilter?> success => Results.Ok(success.Value),
            FailureResult<ContentFilter?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetContentFilterByName(string name, CancellationToken cancellationToken)
    {
        var result = await contentFilterRepository.GetByNameAsync(name, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<ContentFilter?> { Value: null } => Results.NotFound(),
            SuccessResult<ContentFilter?> success => Results.Ok(success.Value),
            FailureResult<ContentFilter?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetContentFiltersByTag(string tag, CancellationToken cancellationToken)
    {
        var result = await contentFilterRepository.GetByTagAsync(tag, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<ContentFilter>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<ContentFilter>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> CreateContentFilter(ContentFilter request, CancellationToken cancellationToken)
    {
        var contentFilter = request with
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        var result = await contentFilterRepository.CreateAsync(contentFilter, createdByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<ContentFilter> success =>
                Results.Created($"/api/content-filters/{success.Value.Id}", success.Value),
            FailureResult<ContentFilter> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status400BadRequest),
            _ =>
                Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> PatchContentFilter(
        Guid id,
        ContentFilterPatch request,
        CancellationToken cancellationToken)
    {
        var patch = request with { Id = id };

        var result = await contentFilterRepository.PatchAsync(patch, modifiedByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<bool> { Value: false } => Results.NotFound(),
            SuccessResult<bool> => Results.NoContent(),
            FailureResult<bool> failure => Results.Problem(failure.Reason, statusCode: StatusCodes.Status400BadRequest),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> DeleteContentFilter(Guid id, CancellationToken cancellationToken)
    {
        var result = await contentFilterRepository.DeleteAsync(id, deletedByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<bool> { Value: false } => Results.NotFound(),
            SuccessResult<bool> => Results.NoContent(),
            FailureResult<bool> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}
