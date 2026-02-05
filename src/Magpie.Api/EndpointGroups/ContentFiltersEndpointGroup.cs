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
        builder.MapGet("my", GetMyContentFilters)
            .WithName(nameof(GetMyContentFilters))
            .Produces<ContentFilter[]>()
            .RequireViewer();

        builder.MapPost("my", CreateMyContentFilter)
            .WithName(nameof(CreateMyContentFilter))
            .Produces<ContentFilter>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireRequester();

        builder.MapGet("my/{id:guid}", GetMyContentFilter)
            .WithName(nameof(GetMyContentFilter))
            .Produces<ContentFilter>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPatch("my/{id:guid}", PatchMyContentFilter)
            .WithName(nameof(PatchMyContentFilter))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireRequester();

        builder.MapDelete("my/{id:guid}", DeleteMyContentFilter)
            .WithName(nameof(DeleteMyContentFilter))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRequester();

        builder.MapGet("", GetSharedContentFilters)
            .WithName(nameof(GetSharedContentFilters))
            .Produces<ContentFilter[]>()
            .RequireViewer();

        builder.MapGet("{id:guid}", GetSharedContentFilter)
            .WithName(nameof(GetSharedContentFilter))
            .Produces<ContentFilter>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPost("", CreateSharedContentFilter)
            .WithName(nameof(CreateSharedContentFilter))
            .Produces<ContentFilter>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapPatch("{id:guid}", PatchSharedContentFilter)
            .WithName(nameof(PatchSharedContentFilter))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapDelete("{id:guid}", DeleteSharedContentFilter)
            .WithName(nameof(DeleteSharedContentFilter))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();
    }

    private async Task<IResult> GetMyContentFilters(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var filters = await contentFilterRepository.ListByUserIdAsync(userId.Value, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(filters);
    }

    private async Task<IResult> CreateMyContentFilter(
        HttpContext httpContext,
        ContentFilter request,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var filter = request with
        {
            Id = Guid.NewGuid(),
            OwnerUserId = userId.Value,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = userId,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        var created = await contentFilterRepository.CreateAsync(filter, userId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"api/{Name}/my/{created.Id}", created);
    }

    private async Task<IResult> GetMyContentFilter(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var filter = await contentFilterRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (filter is null || filter.OwnerUserId != userId)
        {
            return Results.NotFound();
        }

        return Results.Ok(filter);
    }

    private async Task<IResult> PatchMyContentFilter(
        HttpContext httpContext,
        Guid id,
        ContentFilterPatch request,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var existing = await contentFilterRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null || existing.OwnerUserId != userId)
        {
            return Results.NotFound();
        }

        var patch = request with { Id = id };

        var patched = await contentFilterRepository.PatchAsync(patch, userId, cancellationToken)
            .ConfigureAwait(false);

        return patched ? Results.NoContent() : Results.NotFound();
    }

    private async Task<IResult> DeleteMyContentFilter(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var existing = await contentFilterRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null || existing.OwnerUserId != userId)
        {
            return Results.NotFound();
        }

        var deleted = await contentFilterRepository.DeleteAsync(id, userId, cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }

    private async Task<IResult> GetSharedContentFilters(
        string? name,
        string? tag,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(name))
        {
            var filter = await contentFilterRepository.GetByNameAsync(name, cancellationToken)
                .ConfigureAwait(false);
            return filter is null || filter.OwnerUserId is not null
                ? Results.Ok(Array.Empty<ContentFilter>())
                : Results.Ok(new[] { filter });
        }

        if (!string.IsNullOrEmpty(tag))
        {
            var filters = await contentFilterRepository.ListByTagAsync(tag, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(filters);
        }

        var allFilters = await contentFilterRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(allFilters);
    }

    private async Task<IResult> GetSharedContentFilter(Guid id, CancellationToken cancellationToken)
    {
        var filter = await contentFilterRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (filter is null || filter.OwnerUserId is not null)
        {
            return Results.NotFound();
        }

        return Results.Ok(filter);
    }

    private async Task<IResult> CreateSharedContentFilter(
        HttpContext httpContext,
        ContentFilter request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var filter = request with
        {
            Id = Guid.NewGuid(),
            OwnerUserId = null,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = currentUserId,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        var created = await contentFilterRepository.CreateAsync(filter, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"api/{Name}/{created.Id}", created);
    }

    private async Task<IResult> PatchSharedContentFilter(
        HttpContext httpContext,
        Guid id,
        ContentFilterPatch request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var existing = await contentFilterRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (existing is null || existing.OwnerUserId is not null)
        {
            return Results.NotFound();
        }

        var patch = request with { Id = id };

        var patched = await contentFilterRepository.PatchAsync(patch, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return patched ? Results.NoContent() : Results.NotFound();
    }

    private async Task<IResult> DeleteSharedContentFilter(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var existing = await contentFilterRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (existing is null || existing.OwnerUserId is not null)
        {
            return Results.NotFound();
        }

        var deleted = await contentFilterRepository.DeleteAsync(id, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
