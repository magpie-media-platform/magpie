using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Models.Arguments;
using Magpie.Models.Data;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

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

        builder.MapPut("/{id:guid}", UpdateContentFilter)
            .WithName("UpdateContentFilter")
            .WithTags("ContentFilters")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
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

    private async Task<IResult> GetMyContentFilters(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Problem("User not authenticated", statusCode: StatusCodes.Status401Unauthorized);
        }
        var filters = await contentFilterRepository.ListByUserIdAsync(userId.Value, cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(filters);
    }

    private async Task<IResult> GetAllContentFilters(CancellationToken cancellationToken)
    {
        var filters = await contentFilterRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(filters);
    }

    private async Task<IResult> GetContentFilterById(Guid id, CancellationToken cancellationToken)
    {
        var filter = await contentFilterRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return filter is null ? Results.NotFound() : Results.Ok(filter);
    }

    private async Task<IResult> GetContentFilterByName(string name, CancellationToken cancellationToken)
    {
        var filter = await contentFilterRepository.GetByNameAsync(name, cancellationToken).ConfigureAwait(false);
        return filter is null ? Results.NotFound() : Results.Ok(filter);
    }

    private async Task<IResult> GetContentFiltersByTag(string tag, CancellationToken cancellationToken)
    {
        var filters = await contentFilterRepository.ListByTagAsync(tag, cancellationToken).ConfigureAwait(false);
        return Results.Ok(filters);
    }

    private async Task<IResult> CreateContentFilter(
        HttpContext httpContext,
        ContentFilter request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var filter = request with
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = currentUserId,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        var created = await contentFilterRepository.CreateAsync(filter, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"/api/content-filters/{created.Id}", created);
    }

    private async Task<IResult> UpdateContentFilter(
        HttpContext httpContext,
        Guid id,
        ContentFilter request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var existing = await contentFilterRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            return Results.NotFound();
        }

        var filter = request with
        {
            Id = id,
            CreatedAt = existing.CreatedAt,
            CreatedByUserId = existing.CreatedByUserId,
            ModifiedAt = DateTime.UtcNow,
            ModifiedByUserId = currentUserId
        };

        await contentFilterRepository.UpdateAsync(filter, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.NoContent();
    }

    private async Task<IResult> PatchContentFilter(
        HttpContext httpContext,
        Guid id,
        ContentFilterPatch request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var patch = request with { Id = id };

        var patched = await contentFilterRepository.PatchAsync(patch, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return patched ? Results.NoContent() : Results.NotFound();
    }

    private async Task<IResult> DeleteContentFilter(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var deleted = await contentFilterRepository.DeleteAsync(id, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
