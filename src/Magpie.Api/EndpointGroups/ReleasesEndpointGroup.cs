using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class ReleasesEndpointGroup(IReleaseRepository releaseRepository) : IApiEndpointGroup
{
    public string Name => "releases";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetAllReleases)
            .WithName("GetAllReleases")
            .WithTags("Releases")
            .Produces<Release[]>()
            .RequireAuthorization();

        builder.MapGet("/{id:guid}", GetReleaseById)
            .WithName("GetReleaseById")
            .WithTags("Releases")
            .Produces<Release>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapPost("/query", QueryReleases)
            .WithName("QueryReleases")
            .WithTags("Releases")
            .Produces<PagedList<Release>>()
            .RequireAuthorization();

        builder.MapPost("", CreateRelease)
            .WithName("CreateRelease")
            .WithTags("Releases")
            .Produces<Release>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapPut("/{id:guid}", UpdateRelease)
            .WithName("UpdateRelease")
            .WithTags("Releases")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapDelete("/{id:guid}", DeleteRelease)
            .WithName("DeleteRelease")
            .WithTags("Releases")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();
    }

    private async Task<IResult> GetAllReleases(CancellationToken cancellationToken)
    {
        var releases = await releaseRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(releases);
    }

    private async Task<IResult> GetReleaseById(Guid id, CancellationToken cancellationToken)
    {
        var release = await releaseRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return release is null
            ? Results.NotFound()
            : Results.Ok(release);
    }

    private async Task<IResult> QueryReleases(
        ReleaseQuery query,
        int? pageNumber,
        int? pageSize,
        string? sortBy,
        SortDirection? sortDirection,
        CancellationToken cancellationToken)
    {
        var paging = pageNumber.HasValue && pageSize.HasValue
            ? new PagingParameter { PageNumber = pageNumber.Value, PageSize = pageSize.Value }
            : null;

        var sort = !string.IsNullOrEmpty(sortBy)
            ? new SortParameter { PropertyName = sortBy, Direction = sortDirection ?? SortDirection.Ascending }
            : null;

        var result = await releaseRepository.QueryAsync(query, paging, sort, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private async Task<IResult> CreateRelease(
        HttpContext httpContext,
        Release request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var release = request with
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = currentUserId,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        var created = await releaseRepository.CreateAsync(release, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"/api/releases/{created.Id}", created);
    }

    private async Task<IResult> UpdateRelease(
        HttpContext httpContext,
        Guid id,
        Release request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var release = request with
        {
            Id = id,
            ModifiedAt = DateTime.UtcNow,
            ModifiedByUserId = currentUserId
        };

        var updated = await releaseRepository.UpdateAsync(release, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return updated
            ? Results.NoContent()
            : Results.NotFound();
    }

    private async Task<IResult> DeleteRelease(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var deleted = await releaseRepository.DeleteAsync(id, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return deleted
            ? Results.NoContent()
            : Results.NotFound();
    }
}
