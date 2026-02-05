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

internal sealed class DownloadsEndpointGroup(IDownloadRepository downloadRepository) : IApiEndpointGroup
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
}
