using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Data;
using Magpie.Models.Downloads;
using Magpie.Models.Result;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using HttpResult = Microsoft.AspNetCore.Http.IResult;

namespace Magpie.Api.EndpointGroups;

internal sealed class DownloadsEndpointGroup(IDownloadRepository downloadRepository) : IApiEndpointGroup
{
    public string Name => "downloads";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetAllDownloads)
            .WithName("GetAllDownloads")
            .WithTags("Downloads")
            .Produces<Download[]>()
            .RequireAuthorization();

        builder.MapGet("/{id:guid}", GetDownloadById)
            .WithName("GetDownloadById")
            .WithTags("Downloads")
            .Produces<Download>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapGet("/by-content-item/{contentItemId:guid}", GetDownloadsByContentItem)
            .WithName("GetDownloadsByContentItem")
            .WithTags("Downloads")
            .Produces<Download[]>()
            .RequireAuthorization();

        builder.MapGet("/by-download-client/{downloadClientId:guid}", GetDownloadsByDownloadClient)
            .WithName("GetDownloadsByDownloadClient")
            .WithTags("Downloads")
            .Produces<Download[]>()
            .RequireAuthorization();

        builder.MapGet("/by-external-id/{downloadClientId:guid}/{externalId}", GetDownloadByExternalId)
            .WithName("GetDownloadByExternalId")
            .WithTags("Downloads")
            .Produces<Download>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapGet("/by-state/{state}", GetDownloadsByState)
            .WithName("GetDownloadsByState")
            .WithTags("Downloads")
            .Produces<Download[]>()
            .RequireAuthorization();

        builder.MapPost("", CreateDownload)
            .WithName("CreateDownload")
            .WithTags("Downloads")
            .Produces<Download>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapPatch("/{id:guid}/state", UpdateDownloadState)
            .WithName("UpdateDownloadState")
            .WithTags("Downloads")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapDelete("/{id:guid}", DeleteDownload)
            .WithName("DeleteDownload")
            .WithTags("Downloads")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapDelete("/by-download-client/{downloadClientId:guid}", DeleteDownloadsByDownloadClient)
            .WithName("DeleteDownloadsByDownloadClient")
            .WithTags("Downloads")
            .Produces<int>()
            .RequireAuthorization();
    }

    private async Task<HttpResult> GetAllDownloads(CancellationToken cancellationToken)
    {
        var result = await downloadRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Download>> success =>
                Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Download>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetDownloadById(Guid id, CancellationToken cancellationToken)
    {
        var result = await downloadRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<Download?> { Value: null } => Results.NotFound(),
            SuccessResult<Download?> success => Results.Ok(success.Value),
            FailureResult<Download?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetDownloadsByContentItem(Guid contentItemId, CancellationToken cancellationToken)
    {
        var result = await downloadRepository.GetByContentItemIdAsync(contentItemId, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Download>> success =>
                Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Download>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetDownloadsByDownloadClient(
        Guid downloadClientId,
        CancellationToken cancellationToken)
    {
        var result = await downloadRepository.GetByDownloadClientIdAsync(downloadClientId, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Download>> success =>
                Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Download>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetDownloadByExternalId(
        Guid downloadClientId,
        string externalId,
        CancellationToken cancellationToken)
    {
        var result = await downloadRepository.GetByExternalIdAsync(downloadClientId, externalId, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<Download?> { Value: null } => Results.NotFound(),
            SuccessResult<Download?> success => Results.Ok(success.Value),
            FailureResult<Download?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetDownloadsByState(DownloadState state, CancellationToken cancellationToken)
    {
        var result = await downloadRepository.GetByStateAsync(state, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Download>> success =>
                Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Download>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> CreateDownload(
        Download request,
        CancellationToken cancellationToken)
    {
        var download = request with
        {
            Id = Guid.NewGuid(),
            State = request.State == default ? DownloadState.Grabbed : request.State,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        var result = await downloadRepository.CreateAsync(download, createdByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<Download> success =>
                Results.Created($"/api/downloads/{success.Value.Id}", success.Value),
            FailureResult<Download> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status400BadRequest),
            _ =>
                Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> UpdateDownloadState(
        Guid id,
        DownloadStatePatch request,
        CancellationToken cancellationToken)
    {
        var result = await downloadRepository.UpdateStateAsync(
            id,
            request.State,
            request.OutputPath,
            request.FailureReason,
            request.DateCompleted,
            modifiedByUserId: null,
            cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<bool> { Value: false } => Results.NotFound(),
            SuccessResult<bool> => Results.NoContent(),
            FailureResult<bool> failure => Results.Problem(failure.Reason, statusCode: StatusCodes.Status400BadRequest),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> DeleteDownload(Guid id, CancellationToken cancellationToken)
    {
        var result = await downloadRepository.DeleteAsync(id, deletedByUserId: null, cancellationToken)
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

    private async Task<HttpResult> DeleteDownloadsByDownloadClient(
        Guid downloadClientId,
        CancellationToken cancellationToken)
    {
        var result = await downloadRepository
            .DeleteByDownloadClientIdAsync(downloadClientId, deletedByUserId: null, cancellationToken)
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
