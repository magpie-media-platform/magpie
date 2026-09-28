using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Bulk;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class DownloadsEndpointGroup : IApiEndpointGroup
{
    public string Name => "downloads";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", QueryDownloadsAsync)
            .WithName(nameof(QueryDownloadsAsync))
            .Produces<PagedList<DownloadQueueEntry>>()
            .RequireViewer();

        builder.MapPost("bulk/cancel", BulkCancelDownloadsAsync)
            .WithName(nameof(BulkCancelDownloadsAsync))
            .Produces<BulkResult>()
            .RequireManager();

        builder.MapPost("bulk/delete", BulkDeleteDownloadsAsync)
            .WithName(nameof(BulkDeleteDownloadsAsync))
            .Produces<BulkResult>()
            .RequireManager();

        builder.MapPost("bulk/remove", BulkRemoveDownloadsAsync)
            .WithName(nameof(BulkRemoveDownloadsAsync))
            .Produces<BulkResult>()
            .RequireManager();

        builder.MapPost("bulk/pause", BulkPauseDownloadsAsync)
            .WithName(nameof(BulkPauseDownloadsAsync))
            .Produces<BulkResult>()
            .RequireRequester();

        builder.MapPost("bulk/resume", BulkResumeDownloadsAsync)
            .WithName(nameof(BulkResumeDownloadsAsync))
            .Produces<BulkResult>()
            .RequireRequester();

        builder.MapPost("bulk/retry", BulkRetryDownloadsAsync)
            .WithName(nameof(BulkRetryDownloadsAsync))
            .Produces<BulkResult>()
            .RequireManager();

        builder.MapPost("bulk/stop", BulkStopDownloadsAsync)
            .WithName(nameof(BulkStopDownloadsAsync))
            .Produces<BulkResult>()
            .RequireManager();

        builder.MapGet("{id:guid}", GetDownloadAsync)
            .WithName(nameof(GetDownloadAsync))
            .Produces<DownloadQueueEntry>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapDelete("{id:guid}", DeleteDownloadAsync)
            .WithName(nameof(DeleteDownloadAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapDelete("{id:guid}/files", DeleteDownloadWithFilesAsync)
            .WithName(nameof(DeleteDownloadWithFilesAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapPost("{id:guid}/cancel", CancelDownloadAsync)
            .WithName(nameof(CancelDownloadAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapPost("{id:guid}/force-start", ForceStartDownloadAsync)
            .WithName(nameof(ForceStartDownloadAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapPost("{id:guid}/pause", PauseDownloadAsync)
            .WithName(nameof(PauseDownloadAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRequester();

        builder.MapPost("{id:guid}/resume", ResumeDownloadAsync)
            .WithName(nameof(ResumeDownloadAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRequester();

        builder.MapPost("{id:guid}/retry", RetryDownloadAsync)
            .WithName(nameof(RetryDownloadAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();
    }

    internal async Task<IResult> QueryDownloadsAsync(
        [FromServices] IDownloadService downloadService,
        Guid? releaseId,
        Guid? downloadClientId,
        DownloadStatus? status,
        DownloadStatus[]? statuses,
        int? pageNumber,
        int? pageSize,
        string? sortBy,
        SortDirection? sortDirection,
        CancellationToken cancellationToken)
    {
        var query = new DownloadQuery
        {
            ReleaseId = releaseId,
            DownloadClientId = downloadClientId,
            Status = status,
            Statuses = statuses is { Length: > 0 } ? statuses : null,
        };

        var paging = new PagingParameter
        {
            PageNumber = pageNumber ?? 1,
            PageSize = pageSize ?? 25,
        };

        var sort = string.IsNullOrEmpty(sortBy)
            ? null
            : new SortParameter { PropertyName = sortBy, Direction = sortDirection ?? SortDirection.Ascending };

        var page = await downloadService.QueryAsync(query, paging, sort, cancellationToken).ConfigureAwait(false);

        return Results.Ok(page);
    }

    internal async Task<IResult> GetDownloadAsync(
        [FromServices] IDownloadService downloadService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var download = await downloadService.GetAsync(id, cancellationToken).ConfigureAwait(false);

        return download is null ? Results.NotFound() : Results.Ok(download);
    }

    internal async Task<IResult> DeleteDownloadAsync(
        [FromServices] IDownloadService downloadService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var deleted = await downloadService.DeleteAsync(id, cancellationToken: cancellationToken).ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }

    internal async Task<IResult> DeleteDownloadWithFilesAsync(
        [FromServices] IDownloadService downloadService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var deleted = await downloadService
            .DeleteAsync(id, deleteFiles: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }

    internal async Task<IResult> CancelDownloadAsync(
        [FromServices] IDownloadService downloadService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var outcome = await downloadService.CancelWithOutcomeAsync(id, cancellationToken).ConfigureAwait(false);

        return outcome switch
        {
            DownloadCancellationOutcome.Cancelled or DownloadCancellationOutcome.AlreadyCancelled =>
                Results.NoContent(),
            DownloadCancellationOutcome.NotFound => Results.NotFound(),
            DownloadCancellationOutcome.NotCancellable => Results.Problem(
                "The download status does not permit cancellation.", statusCode: StatusCodes.Status409Conflict),
            DownloadCancellationOutcome.ClientUnavailable => Results.Problem(
                "The download client or transfer identifier is unavailable. Cancellation can be retried.",
                statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Results.Problem(
                "The download client could not cancel the transfer. Cancellation can be retried.",
                statusCode: StatusCodes.Status502BadGateway)
        };
    }

    internal async Task<IResult> ForceStartDownloadAsync(
        [FromServices] IDownloadService downloadService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var started = await downloadService.ForceStartAsync(id, cancellationToken).ConfigureAwait(false);

        return started ? Results.NoContent() : Results.NotFound();
    }

    internal async Task<IResult> PauseDownloadAsync(
        [FromServices] IDownloadService downloadService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var paused = await downloadService.PauseAsync(id, cancellationToken).ConfigureAwait(false);

        return paused ? Results.NoContent() : Results.NotFound();
    }

    internal async Task<IResult> ResumeDownloadAsync(
        [FromServices] IDownloadService downloadService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var resumed = await downloadService.ResumeAsync(id, cancellationToken).ConfigureAwait(false);

        return resumed ? Results.NoContent() : Results.NotFound();
    }

    internal async Task<IResult> RetryDownloadAsync(
        [FromServices] IDownloadService downloadService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var retried = await downloadService.RetryAsync(id, cancellationToken).ConfigureAwait(false);

        return retried ? Results.NoContent() : Results.NotFound();
    }

    internal async Task<IResult> BulkCancelDownloadsAsync(
        [FromServices] IDownloadService downloadService,
        BulkAction<DownloadQuery> action,
        CancellationToken cancellationToken)
    {
        var result = await downloadService.BulkCancelAsync(action, cancellationToken).ConfigureAwait(false);

        return Results.Ok(result);
    }

    internal async Task<IResult> BulkDeleteDownloadsAsync(
        [FromServices] IDownloadService downloadService,
        BulkAction<DownloadQuery> action,
        CancellationToken cancellationToken)
    {
        var result = await downloadService.BulkDeleteAsync(action, cancellationToken).ConfigureAwait(false);

        return Results.Ok(result);
    }

    internal async Task<IResult> BulkRemoveDownloadsAsync(
        [FromServices] IDownloadService downloadService,
        BulkAction<DownloadQuery> action,
        CancellationToken cancellationToken)
    {
        var result = await downloadService.BulkRemoveAsync(action, cancellationToken).ConfigureAwait(false);

        return Results.Ok(result);
    }

    internal async Task<IResult> BulkPauseDownloadsAsync(
        [FromServices] IDownloadService downloadService,
        BulkAction<DownloadQuery> action,
        CancellationToken cancellationToken)
    {
        var result = await downloadService.BulkPauseAsync(action, cancellationToken).ConfigureAwait(false);

        return Results.Ok(result);
    }

    internal async Task<IResult> BulkResumeDownloadsAsync(
        [FromServices] IDownloadService downloadService,
        BulkAction<DownloadQuery> action,
        CancellationToken cancellationToken)
    {
        var result = await downloadService.BulkResumeAsync(action, cancellationToken).ConfigureAwait(false);

        return Results.Ok(result);
    }

    internal async Task<IResult> BulkRetryDownloadsAsync(
        [FromServices] IDownloadService downloadService,
        BulkAction<DownloadQuery> action,
        CancellationToken cancellationToken)
    {
        var result = await downloadService.BulkRetryAsync(action, cancellationToken).ConfigureAwait(false);

        return Results.Ok(result);
    }

    internal async Task<IResult> BulkStopDownloadsAsync(
        [FromServices] IDownloadService downloadService,
        BulkAction<DownloadQuery> action,
        CancellationToken cancellationToken)
    {
        var result = await downloadService.BulkStopAsync(action, cancellationToken).ConfigureAwait(false);

        return Results.Ok(result);
    }
}
