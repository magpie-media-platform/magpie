using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Bulk;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Import;
using Magpie.Models.Paging;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

[MagpieCommandGroup("library-import", "import", "Import existing files into the library")]
internal sealed class LibraryImportEndpointGroup : IApiEndpointGroup
{
    public string Name => "import";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("candidates", QueryLibraryImportCandidatesAsync)
            .WithName(nameof(QueryLibraryImportCandidatesAsync))
            .Produces<PagedList<LibraryImportCandidate>>()
            .RequireViewer();

        builder.MapPost("candidates/bulk-accept", BulkAcceptLibraryImportCandidatesAsync)
            .WithName(nameof(BulkAcceptLibraryImportCandidatesAsync))
            .Produces<LibraryImportSubmissionResult>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapPost("candidates/{id:guid}/match", SetLibraryImportMatchAsync)
            .WithName(nameof(SetLibraryImportMatchAsync))
            .Produces<LibraryImportCandidate>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();
    }

    [MagpieCommand("candidates", "GET", "candidates", "List library import candidates")]
    internal async Task<IResult> QueryLibraryImportCandidatesAsync(
        [FromServices] ILibraryImportService libraryImportService,
        Guid? rootFolderId,
        Guid? scanRunId,
        string[]? status,
        bool? hasMatches,
        int? pageNumber,
        int? pageSize,
        string? sortBy,
        SortDirection? sortDirection,
        CancellationToken cancellationToken)
    {
        var parsedStatuses = new List<LibraryImportCandidateStatus>();
        if (status is { Length: > 0 })
        {
            foreach (var value in status)
            {
                if (Enum.TryParse<LibraryImportCandidateStatus>(value, ignoreCase: true, out var parsed))
                {
                    parsedStatuses.Add(parsed);
                }
            }
        }

        var statuses = parsedStatuses.Distinct().ToArray();

        var query = new LibraryImportCandidateQuery
        {
            RootFolderId = rootFolderId,
            ScanRunId = scanRunId,
            Statuses = statuses is { Length: > 0 } ? statuses : null,
            HasMatches = hasMatches,
        };

        var paging = new PagingParameter
        {
            PageNumber = pageNumber ?? 1,
            PageSize = pageSize ?? 25,
        };

        var sort = !string.IsNullOrEmpty(sortBy)
            ? new SortParameter { PropertyName = sortBy, Direction = sortDirection ?? SortDirection.Ascending }
            : null;

        var result = await libraryImportService.QueryCandidatesAsync(query, paging, sort, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    [MagpieCommand(
        "bulk-accept",
        "POST",
        "candidates/bulk-accept",
        "Queue multiple library import candidates for import")]
    internal async Task<IResult> BulkAcceptLibraryImportCandidatesAsync(
        [FromServices] ILibraryImportService libraryImportService,
        AuthenticatedUser user,
        [FromBody] BulkAction<LibraryImportCandidateQuery> action,
        CancellationToken cancellationToken)
    {
        var result = await libraryImportService
            .SubmitImportAsync(action, user.Id, correlationId: null, cancellationToken)
            .ConfigureAwait(false);

        return Results.Json(result, statusCode: StatusCodes.Status202Accepted);
    }

    [MagpieCommand("match", "POST", "candidates/{id:guid}/match", "Set the match for a library import candidate")]
    internal async Task<IResult> SetLibraryImportMatchAsync(
        [FromServices] ILibraryImportService libraryImportService,
        AuthenticatedUser user,
        Guid id,
        [FromBody] SetLibraryImportMatchRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await libraryImportService
            .MatchAsync(
                id,
                request.ProviderType,
                request.ExternalIdKey,
                request.ExternalId,
                request.Title,
                request.Subtitle,
                request.ImageUrl,
                request.LevelSlug,
                user.Id,
                correlationId: null,
                cancellationToken)
            .ConfigureAwait(false);

        return updated is null ? Results.NotFound() : Results.Ok(updated);
    }
}
