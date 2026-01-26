using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Data;
using Magpie.Models.Indexers;
using Magpie.Models.Result;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using HttpResult = Microsoft.AspNetCore.Http.IResult;

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

        builder.MapGet("/by-indexer/{indexerId:guid}", GetReleasesByIndexer)
            .WithName("GetReleasesByIndexer")
            .WithTags("Releases")
            .Produces<Release[]>()
            .RequireAuthorization();

        builder.MapGet("/by-external-id/{indexerId:guid}/{externalId}", GetReleaseByExternalId)
            .WithName("GetReleaseByExternalId")
            .WithTags("Releases")
            .Produces<Release>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapGet("/by-provider/{provider}", GetReleasesByProvider)
            .WithName("GetReleasesByProvider")
            .WithTags("Releases")
            .Produces<Release[]>()
            .RequireAuthorization();

        builder.MapGet("/by-info-hash/{infoHash}", GetReleasesByInfoHash)
            .WithName("GetReleasesByInfoHash")
            .WithTags("Releases")
            .Produces<Release[]>()
            .RequireAuthorization();

        builder.MapGet("/by-tag/{tag}", GetReleasesByTag)
            .WithName("GetReleasesByTag")
            .WithTags("Releases")
            .Produces<Release[]>()
            .RequireAuthorization();

        builder.MapPost("", CreateRelease)
            .WithName("CreateRelease")
            .WithTags("Releases")
            .Produces<Release>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapPatch("/{id:guid}", PatchRelease)
            .WithName("PatchRelease")
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

        builder.MapDelete("/by-indexer/{indexerId:guid}", DeleteReleasesByIndexer)
            .WithName("DeleteReleasesByIndexer")
            .WithTags("Releases")
            .Produces<int>()
            .RequireAuthorization();
    }

    private async Task<HttpResult> GetAllReleases(CancellationToken cancellationToken)
    {
        var result = await releaseRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Release>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Release>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetReleaseById(Guid id, CancellationToken cancellationToken)
    {
        var result = await releaseRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<Release?> { Value: null } => Results.NotFound(),
            SuccessResult<Release?> success => Results.Ok(success.Value),
            FailureResult<Release?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetReleasesByIndexer(Guid indexerId, CancellationToken cancellationToken)
    {
        var result = await releaseRepository.GetByIndexerIdAsync(indexerId, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Release>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Release>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetReleaseByExternalId(
        Guid indexerId,
        string externalId,
        CancellationToken cancellationToken)
    {
        var result = await releaseRepository.GetByExternalIdAsync(indexerId, externalId, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<Release?> { Value: null } => Results.NotFound(),
            SuccessResult<Release?> success => Results.Ok(success.Value),
            FailureResult<Release?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetReleasesByProvider(string provider, CancellationToken cancellationToken)
    {
        var result = await releaseRepository.GetByProviderAsync(provider, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Release>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Release>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetReleasesByInfoHash(string infoHash, CancellationToken cancellationToken)
    {
        var result = await releaseRepository.GetByInfoHashAsync(infoHash, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Release>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Release>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetReleasesByTag(string tag, CancellationToken cancellationToken)
    {
        var result = await releaseRepository.GetByTagAsync(tag, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Release>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Release>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> CreateRelease(Release request, CancellationToken cancellationToken)
    {
        var release = request with
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        var result = await releaseRepository.CreateAsync(release, createdByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<Release> success => Results.Created($"/api/releases/{success.Value.Id}", success.Value),
            FailureResult<Release> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status400BadRequest),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> PatchRelease(Guid id, Release request, CancellationToken cancellationToken)
    {
        var existingResult = await releaseRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (existingResult is SuccessResult<Release?> { Value: null })
        {
            return Results.NotFound();
        }

        if (existingResult is FailureResult<Release?> existingFailure)
        {
            return Results.Problem(existingFailure.Reason, statusCode: StatusCodes.Status500InternalServerError);
        }

        if (existingResult is not SuccessResult<Release?> { Value: { } existing })
        {
            return Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError);
        }

        var release = request with
        {
            Id = id,
            CreatedAt = existing.CreatedAt,
            CreatedByUserId = existing.CreatedByUserId,
            ModifiedAt = DateTimeOffset.UtcNow,
            ModifiedByUserId = null
        };

        var result = await releaseRepository.UpdateAsync(release, modifiedByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<bool> => Results.NoContent(),
            FailureResult<bool> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status400BadRequest),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> DeleteRelease(Guid id, CancellationToken cancellationToken)
    {
        var result = await releaseRepository.DeleteAsync(id, deletedByUserId: null, cancellationToken)
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

    private async Task<HttpResult> DeleteReleasesByIndexer(Guid indexerId, CancellationToken cancellationToken)
    {
        var result = await releaseRepository.DeleteByIndexerIdAsync(indexerId, deletedByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<int> success => Results.Ok(success.Value),
            FailureResult<int> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}
