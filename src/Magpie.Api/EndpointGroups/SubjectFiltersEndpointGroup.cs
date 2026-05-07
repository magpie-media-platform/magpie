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

internal sealed class SubjectFiltersEndpointGroup(
    ISubjectFilterRepository subjectFilterRepository) : IApiEndpointGroup
{
    public string Name => "subject-filters";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("my", GetMySubjectFilters)
            .WithName(nameof(GetMySubjectFilters))
            .Produces<SubjectFilter[]>()
            .RequireViewer();

        builder.MapPost("my", CreateMySubjectFilter)
            .WithName(nameof(CreateMySubjectFilter))
            .Produces<SubjectFilter>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireRequester();

        builder.MapGet("my/{id:guid}", GetMySubjectFilter)
            .WithName(nameof(GetMySubjectFilter))
            .Produces<SubjectFilter>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPatch("my/{id:guid}", PatchMySubjectFilter)
            .WithName(nameof(PatchMySubjectFilter))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireRequester();

        builder.MapDelete("my/{id:guid}", DeleteMySubjectFilter)
            .WithName(nameof(DeleteMySubjectFilter))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRequester();

        builder.MapGet("", GetSharedSubjectFilters)
            .WithName(nameof(GetSharedSubjectFilters))
            .Produces<SubjectFilter[]>()
            .RequireViewer();

        builder.MapGet("{id:guid}", GetSharedSubjectFilter)
            .WithName(nameof(GetSharedSubjectFilter))
            .Produces<SubjectFilter>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPost("", CreateSharedSubjectFilter)
            .WithName(nameof(CreateSharedSubjectFilter))
            .Produces<SubjectFilter>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapPatch("{id:guid}", PatchSharedSubjectFilter)
            .WithName(nameof(PatchSharedSubjectFilter))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapDelete("{id:guid}", DeleteSharedSubjectFilter)
            .WithName(nameof(DeleteSharedSubjectFilter))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        // TODO: Add preview endpoints when ISubjectRepository implements IQueryRepository
    }

    private async Task<IResult> GetMySubjectFilters(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var filters = await subjectFilterRepository.ListByUserIdAsync(userId.Value, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(filters);
    }

    private async Task<IResult> CreateMySubjectFilter(
        HttpContext httpContext,
        SubjectFilter request,
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

        var created = await subjectFilterRepository.CreateAsync(filter, userId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"api/{Name}/my/{created.Id}", created);
    }

    private async Task<IResult> GetMySubjectFilter(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var filter = await subjectFilterRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (filter is null || filter.OwnerUserId != userId)
        {
            return Results.NotFound();
        }

        return Results.Ok(filter);
    }

    private async Task<IResult> PatchMySubjectFilter(
        HttpContext httpContext,
        Guid id,
        SubjectFilterPatch request,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var existing = await subjectFilterRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null || existing.OwnerUserId != userId)
        {
            return Results.NotFound();
        }

        var patch = request with { Id = id };

        var patched = await subjectFilterRepository.PatchAsync(patch, userId, cancellationToken)
            .ConfigureAwait(false);

        return patched ? Results.NoContent() : Results.NotFound();
    }

    private async Task<IResult> DeleteMySubjectFilter(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var existing = await subjectFilterRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null || existing.OwnerUserId != userId)
        {
            return Results.NotFound();
        }

        var deleted = await subjectFilterRepository.DeleteAsync(id, userId, cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }

    private async Task<IResult> GetSharedSubjectFilters(
        string? name,
        string? tag,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(name))
        {
            var filter = await subjectFilterRepository.GetByNameAsync(name, cancellationToken)
                .ConfigureAwait(false);
            return filter is null || filter.OwnerUserId is not null
                ? Results.Ok(Array.Empty<SubjectFilter>())
                : Results.Ok(new[] { filter });
        }

        if (!string.IsNullOrEmpty(tag))
        {
            var filters = await subjectFilterRepository.ListByTagAsync(tag, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(filters);
        }

        var allFilters = await subjectFilterRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(allFilters);
    }

    private async Task<IResult> GetSharedSubjectFilter(Guid id, CancellationToken cancellationToken)
    {
        var filter = await subjectFilterRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (filter is null || filter.OwnerUserId is not null)
        {
            return Results.NotFound();
        }

        return Results.Ok(filter);
    }

    private async Task<IResult> CreateSharedSubjectFilter(
        HttpContext httpContext,
        SubjectFilter request,
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

        var created = await subjectFilterRepository.CreateAsync(filter, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"api/{Name}/{created.Id}", created);
    }

    private async Task<IResult> PatchSharedSubjectFilter(
        HttpContext httpContext,
        Guid id,
        SubjectFilterPatch request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var existing = await subjectFilterRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (existing is null || existing.OwnerUserId is not null)
        {
            return Results.NotFound();
        }

        var patch = request with { Id = id };

        var patched = await subjectFilterRepository.PatchAsync(patch, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return patched ? Results.NoContent() : Results.NotFound();
    }

    private async Task<IResult> DeleteSharedSubjectFilter(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var existing = await subjectFilterRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (existing is null || existing.OwnerUserId is not null)
        {
            return Results.NotFound();
        }

        var deleted = await subjectFilterRepository.DeleteAsync(id, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
