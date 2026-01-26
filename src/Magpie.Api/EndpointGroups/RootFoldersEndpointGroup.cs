using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Data;
using Magpie.Models.Result;
using Magpie.Models.RootFolders;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using HttpResult = Microsoft.AspNetCore.Http.IResult;

namespace Magpie.Api.EndpointGroups;

internal sealed class RootFoldersEndpointGroup(IRootFolderRepository rootFolderRepository) : IApiEndpointGroup
{
    public string Name => "root-folders";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetAllRootFolders)
            .WithName("GetAllRootFolders")
            .WithTags("RootFolders")
            .Produces<RootFolder[]>()
            .RequireAuthorization();

        builder.MapGet("/{id:guid}", GetRootFolderById)
            .WithName("GetRootFolderById")
            .WithTags("RootFolders")
            .Produces<RootFolder>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapGet("/by-content-type/{contentTypeId:guid}", GetRootFoldersByContentType)
            .WithName("GetRootFoldersByContentType")
            .WithTags("RootFolders")
            .Produces<RootFolder[]>()
            .RequireAuthorization();

        builder.MapPost("", CreateRootFolder)
            .WithName("CreateRootFolder")
            .WithTags("RootFolders")
            .Produces<RootFolder>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapPatch("/{id:guid}", PatchRootFolder)
            .WithName("PatchRootFolder")
            .WithTags("RootFolders")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapDelete("/{id:guid}", DeleteRootFolder)
            .WithName("DeleteRootFolder")
            .WithTags("RootFolders")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();
    }

    private async Task<HttpResult> GetAllRootFolders(CancellationToken cancellationToken)
    {
        var result = await rootFolderRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<RootFolder>> success =>
                Results.Ok(success.Value),
            FailureResult<IReadOnlyList<RootFolder>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetRootFolderById(Guid id, CancellationToken cancellationToken)
    {
        var result = await rootFolderRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<RootFolder?> { Value: null } => Results.NotFound(),
            SuccessResult<RootFolder?> success => Results.Ok(success.Value),
            FailureResult<RootFolder?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetRootFoldersByContentType(Guid contentTypeId, CancellationToken cancellationToken)
    {
        var result = await rootFolderRepository.GetByContentTypeIdAsync(contentTypeId, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<RootFolder>> success =>
                Results.Ok(success.Value),
            FailureResult<IReadOnlyList<RootFolder>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> CreateRootFolder(
        RootFolder request,
        CancellationToken cancellationToken)
    {
        var rootFolder = request with
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        var result = await rootFolderRepository.CreateAsync(rootFolder, createdByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<RootFolder> success =>
                Results.Created($"/api/root-folders/{success.Value.Id}", success.Value),
            FailureResult<RootFolder> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status400BadRequest),
            _ =>
                Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> PatchRootFolder(
        Guid id,
        RootFolderPatch request,
        CancellationToken cancellationToken)
    {
        var patch = request with { Id = id };

        var result = await rootFolderRepository.PatchAsync(patch, modifiedByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<bool> { Value: false } => Results.NotFound(),
            SuccessResult<bool> => Results.NoContent(),
            FailureResult<bool> failure => Results.Problem(failure.Reason, statusCode: StatusCodes.Status400BadRequest),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> DeleteRootFolder(Guid id, CancellationToken cancellationToken)
    {
        var result = await rootFolderRepository.DeleteAsync(id, deletedByUserId: null, cancellationToken)
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
