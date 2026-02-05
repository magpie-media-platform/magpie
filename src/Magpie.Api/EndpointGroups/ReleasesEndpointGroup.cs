using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class ReleasesEndpointGroup(IReleaseRepository releaseRepository) : IApiEndpointGroup
{
    public string Name => "releases";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetReleases)
            .WithName(nameof(GetReleases))
            .Produces<Release[]>()
            .RequireViewer();

        builder.MapGet("{id:guid}", GetReleaseById)
            .WithName(nameof(GetReleaseById))
            .Produces<Release>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPost("", CreateRelease)
            .WithName(nameof(CreateRelease))
            .Produces<Release>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapPatch("{id:guid}", PatchRelease)
            .WithName(nameof(PatchRelease))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapDelete("{id:guid}", DeleteRelease)
            .WithName(nameof(DeleteRelease))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();
    }

    private async Task<IResult> GetReleases(
        Guid? indexerId,
        DateTime? after,
        CancellationToken cancellationToken)
    {
        if (indexerId is null && after is null)
        {
            var allReleases = await releaseRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(allReleases.ToArray());
        }

        var query = new ReleaseQuery
        {
            IndexerId = indexerId,
            PublishDate = after is not null ? new Range<DateTime>(after, null) : null
        };

        var result = await releaseRepository.QueryAsync(query, null, null, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result.Items.ToArray());
    }

    private async Task<IResult> GetReleaseById(Guid id, CancellationToken cancellationToken)
    {
        var release = await releaseRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (release is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(release);
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

        return Results.Created($"api/{Name}/{created.Id}", created);
    }

    private async Task<IResult> PatchRelease(
        HttpContext httpContext,
        Guid id,
        Release request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var release = request with { Id = id };

        var updated = await releaseRepository.UpdateAsync(release, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        if (!updated)
        {
            return Results.NotFound();
        }

        return Results.NoContent();
    }

    private async Task<IResult> DeleteRelease(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var deleted = await releaseRepository.DeleteAsync(id, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        if (!deleted)
        {
            return Results.NotFound();
        }

        return Results.NoContent();
    }
}
