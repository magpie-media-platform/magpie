using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Integrations;
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

internal sealed class DownloadsEndpointGroup(
    IDownloadRepository downloadRepository,
    IDownloadClientRepository downloadClientRepository,
    IEnumerable<IDownloadClientIntegration> downloadClientIntegrations) : IApiEndpointGroup
{
    public string Name => "downloads";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", QueryDownloads)
            .WithName(nameof(QueryDownloads))
            .Produces<PagedList<Download>>()
            .RequireViewer();

        builder.MapGet("{id:guid}", GetDownloadById)
            .WithName(nameof(GetDownloadById))
            .Produces<Download>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPost("", CreateDownload)
            .WithName(nameof(CreateDownload))
            .Produces<Download>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireRequester();

        builder.MapDelete("{id:guid}", DeleteDownload)
            .WithName(nameof(DeleteDownload))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapPost("{id:guid}/pause", PauseDownload)
            .WithName(nameof(PauseDownload))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .RequireRequester();

        builder.MapPost("{id:guid}/resume", ResumeDownload)
            .WithName(nameof(ResumeDownload))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .RequireRequester();
    }

    private async Task<IResult> QueryDownloads(
        Guid? releaseId,
        Guid? downloadClientId,
        DownloadStatus? status,
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
            Status = status
        };

        var paging = pageNumber is not null && pageSize is not null
            ? new PagingParameter { PageNumber = pageNumber.Value, PageSize = pageSize.Value }
            : null;

        var sort = !string.IsNullOrEmpty(sortBy)
            ? new SortParameter { PropertyName = sortBy, Direction = sortDirection ?? SortDirection.Ascending }
            : null;

        var result = await downloadRepository.QueryAsync(query, paging, sort, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private async Task<IResult> GetDownloadById(Guid id, CancellationToken cancellationToken)
    {
        var download = await downloadRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return download is null
            ? Results.NotFound()
            : Results.Ok(download);
    }

    private async Task<IResult> CreateDownload(
        HttpContext httpContext,
        Download request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var download = request with
        {
            Id = Guid.NewGuid(),
            Status = request.Status == default ? DownloadStatus.Grabbed : request.Status,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = currentUserId,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        var created = await downloadRepository.CreateAsync(download, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"api/{Name}/{created.Id}", created);
    }

    private async Task<IResult> DeleteDownload(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var deleted = await downloadRepository.DeleteAsync(id, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return deleted
            ? Results.NoContent()
            : Results.NotFound();
    }

    private async Task<IResult> PauseDownload(Guid id, CancellationToken cancellationToken)
    {
        return await DispatchTransitionAsync(
            id,
            "pause",
            (integration, externalId, settings, ct) => integration.PauseDownloadAsync(externalId, settings, ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IResult> ResumeDownload(Guid id, CancellationToken cancellationToken)
    {
        return await DispatchTransitionAsync(
            id,
            "resume",
            (integration, externalId, settings, ct) => integration.ResumeDownloadAsync(externalId, settings, ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IResult> DispatchTransitionAsync(
        Guid id,
        string action,
        Func<IDownloadClientIntegration, string, IReadOnlyDictionary<string, string>, CancellationToken, Task<bool>> dispatch,
        CancellationToken cancellationToken)
    {
        var download = await downloadRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (download is null)
        {
            return Results.NotFound();
        }

        var downloadClient = await downloadClientRepository
            .GetByIdAsync(download.DownloadClientId, cancellationToken)
            .ConfigureAwait(false);

        if (downloadClient is null)
        {
            return Results.NotFound();
        }

        var integration = downloadClientIntegrations
            .FirstOrDefault(i => string.Equals(
                i.ProviderType,
                downloadClient.ProviderType,
                StringComparison.OrdinalIgnoreCase));

        if (integration is null)
        {
            return Results.NotFound();
        }

        var success = await dispatch(integration, download.ExternalId, downloadClient.Settings, cancellationToken)
            .ConfigureAwait(false);

        return success
            ? Results.NoContent()
            : Results.Problem(
                detail: $"Download client '{downloadClient.Name}' rejected the {action} request.",
                statusCode: StatusCodes.Status502BadGateway);
    }
}
