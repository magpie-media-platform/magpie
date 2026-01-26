using System;
using System.Collections.Generic;
using System.Linq;
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

internal sealed class MetadataFiltersEndpointGroup(
    IMetadataFilterRepository metadataFilterRepository) : IApiEndpointGroup
{
    public string Name => "metadata-filters";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetAllMetadataFilters)
            .WithName("GetAllMetadataFilters")
            .WithTags("MetadataFilters")
            .Produces<MetadataFilter[]>()
            .RequireAuthorization();

        builder.MapGet("/{id:guid}", GetMetadataFilterById)
            .WithName("GetMetadataFilterById")
            .WithTags("MetadataFilters")
            .Produces<MetadataFilter>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapGet("/by-name/{name}", GetMetadataFilterByName)
            .WithName("GetMetadataFilterByName")
            .WithTags("MetadataFilters")
            .Produces<MetadataFilter>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapGet("/by-tag/{tag}", GetMetadataFiltersByTag)
            .WithName("GetMetadataFiltersByTag")
            .WithTags("MetadataFilters")
            .Produces<MetadataFilter[]>()
            .RequireAuthorization();

        builder.MapPost("", CreateMetadataFilter)
            .WithName("CreateMetadataFilter")
            .WithTags("MetadataFilters")
            .Produces<MetadataFilter>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapPatch("/{id:guid}", PatchMetadataFilter)
            .WithName("PatchMetadataFilter")
            .WithTags("MetadataFilters")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapDelete("/{id:guid}", DeleteMetadataFilter)
            .WithName("DeleteMetadataFilter")
            .WithTags("MetadataFilters")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();
    }

    private async Task<HttpResult> GetAllMetadataFilters(CancellationToken cancellationToken)
    {
        var result = await metadataFilterRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<MetadataFilter>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<MetadataFilter>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetMetadataFilterById(Guid id, CancellationToken cancellationToken)
    {
        var result = await metadataFilterRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<MetadataFilter?> { Value: null } => Results.NotFound(),
            SuccessResult<MetadataFilter?> success => Results.Ok(success.Value),
            FailureResult<MetadataFilter?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetMetadataFilterByName(string name, CancellationToken cancellationToken)
    {
        var result = await metadataFilterRepository.GetByNameAsync(name, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<MetadataFilter?> { Value: null } => Results.NotFound(),
            SuccessResult<MetadataFilter?> success => Results.Ok(success.Value),
            FailureResult<MetadataFilter?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetMetadataFiltersByTag(string tag, CancellationToken cancellationToken)
    {
        var result = await metadataFilterRepository.GetByTagAsync(tag, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<MetadataFilter>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<MetadataFilter>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> CreateMetadataFilter(MetadataFilter request, CancellationToken cancellationToken)
    {
        var metadataFilter = request with
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        var result = await metadataFilterRepository.CreateAsync(
            metadataFilter,
            createdByUserId: null,
            cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<MetadataFilter> success =>
                Results.Created($"/api/metadata-filters/{success.Value.Id}", success.Value),
            FailureResult<MetadataFilter> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status400BadRequest),
            _ =>
                Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> PatchMetadataFilter(
        Guid id,
        MetadataFilterPatch request,
        CancellationToken cancellationToken)
    {
        var patch = request with { Id = id };

        var result = await metadataFilterRepository.PatchAsync(patch, modifiedByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<bool> { Value: false } => Results.NotFound(),
            SuccessResult<bool> => Results.NoContent(),
            FailureResult<bool> failure => Results.Problem(failure.Reason, statusCode: StatusCodes.Status400BadRequest),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> DeleteMetadataFilter(Guid id, CancellationToken cancellationToken)
    {
        var result = await metadataFilterRepository.DeleteAsync(id, deletedByUserId: null, cancellationToken)
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
