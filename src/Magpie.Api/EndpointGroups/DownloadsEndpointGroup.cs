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
using Magpie.Models.Sorting;

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
            .WithName("QueryDownloads")
            .WithTags("Downloads")
            .Produces<PagedList<Download>>()
            .RequireAuthorization();

        builder.MapGet("/{id:guid}", GetDownloadById)
            .WithName("GetDownloadById")
            .WithTags("Downloads")
            .Produces<Download>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapPost("", CreateDownload)
            .WithName("CreateDownload")
            .WithTags("Downloads")
            .Produces<Download>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapDelete("/{id:guid}", DeleteDownload)
            .WithName("DeleteDownload")
            .WithTags("Downloads")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();
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
        var query = new DownloadQuery(
            ReleaseId: releaseId,
            DownloadClientId: downloadClientId,
            Status: status);

        var paging = pageNumber.HasValue && pageSize.HasValue
            ? new PagingParameter(pageNumber.Value, pageSize.Value)
            : null;

        var sort = !string.IsNullOrEmpty(sortBy)
            ? new SortParameter(sortBy, sortDirection ?? SortDirection.Ascending)
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

        return Results.Created($"/api/downloads/{created.Id}", created);
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
