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

    internal async Task<IResult> PreviewImportRenamesAsync(
        [FromServices] ILibraryImportService service,
        [FromBody] LibraryImportRenameSelection selection,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.PreviewRenamesAsync(selection, cancellationToken).ConfigureAwait(false));

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapPost("rename-preview", PreviewImportRenamesAsync)
            .WithName(nameof(PreviewImportRenamesAsync))
            .Produces<FileRenamePreview>()
            .RequireManager();

        builder.MapGet("candidates", QueryLibraryImportCandidatesAsync)
            .WithName(nameof(QueryLibraryImportCandidatesAsync))
            .Produces<PagedList<LibraryImportCandidate>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireViewer();

        builder.MapGet("candidate-groups", QueryLibraryImportCandidateGroupsAsync)
            .WithName(nameof(QueryLibraryImportCandidateGroupsAsync))
            .Produces<PagedList<LibraryImportCandidateGroup>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireViewer();

        builder.MapPost("candidates/bulk-accept", BulkAcceptLibraryImportCandidatesAsync)
            .WithName(nameof(BulkAcceptLibraryImportCandidatesAsync))
            .Produces<LibraryImportSubmissionResult>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapPost("candidate-groups/accept", AcceptLibraryImportCandidateGroupAsync)
            .WithName(nameof(AcceptLibraryImportCandidateGroupAsync))
            .Produces<LibraryImportSubmissionResult>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapPost("candidate-groups/bulk-accept", BulkAcceptLibraryImportCandidateGroupsAsync)
            .WithName(nameof(BulkAcceptLibraryImportCandidateGroupsAsync))
            .Produces<LibraryImportSubmissionResult>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapPost("candidates/{id:guid}/match", SetLibraryImportMatchAsync)
            .WithName(nameof(SetLibraryImportMatchAsync))
            .Produces<LibraryImportCandidate>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapPost("candidate-groups/match", SetLibraryImportCandidateGroupMatchAsync)
            .WithName(nameof(SetLibraryImportCandidateGroupMatchAsync))
            .Produces<LibraryImportCandidateGroup>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapPost("candidates/reject", RejectLibraryImportCandidatesAsync)
            .WithName(nameof(RejectLibraryImportCandidatesAsync))
            .Produces<BulkResult>()
            .RequireManager();

        builder.MapPost("candidates/restore", RestoreLibraryImportCandidatesAsync)
            .WithName(nameof(RestoreLibraryImportCandidatesAsync))
            .Produces<BulkResult>()
            .RequireManager();

        builder.MapGet("content/{id:guid}/candidates", QueryContentLibraryImportCandidatesAsync)
            .WithName(nameof(QueryContentLibraryImportCandidatesAsync))
            .Produces<IReadOnlyList<LibraryImportCandidate>>()
            .RequireViewer();

        builder.MapPost("content/{id:guid}/associate", AssociateContentLibraryImportCandidatesAsync)
            .WithName(nameof(AssociateContentLibraryImportCandidatesAsync))
            .Produces<LibraryImportSubmissionResult>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();
    }

    [MagpieCommand("reject", "POST", "candidates/reject", "Reject library import candidates until restored")]
    internal async Task<IResult> RejectLibraryImportCandidatesAsync(
        [FromServices] ILibraryImportService libraryImportService,
        AuthenticatedUser user,
        [FromBody] BulkAction<LibraryImportCandidateQuery> action,
        CancellationToken cancellationToken)
    {
        var count = await libraryImportService.RejectAsync(action, user.Id, cancellationToken).ConfigureAwait(false);
        return Results.Ok(new BulkResult { Count = count });
    }

    [MagpieCommand("restore", "POST", "candidates/restore", "Restore rejected library import candidates")]
    internal async Task<IResult> RestoreLibraryImportCandidatesAsync(
        [FromServices] ILibraryImportService libraryImportService,
        AuthenticatedUser user,
        [FromBody] BulkAction<LibraryImportCandidateQuery> action,
        CancellationToken cancellationToken)
    {
        var count = await libraryImportService
            .RestoreAsync(action, user.Id, correlationId: null, cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(new BulkResult { Count = count });
    }

    [MagpieCommand(
        "content-candidates",
        "GET",
        "content/{id:guid}/candidates",
        "List the discovered files that wait for association with a library item or its descendants")]
    internal async Task<IResult> QueryContentLibraryImportCandidatesAsync(
        [FromServices] ILibraryImportService libraryImportService,
        Guid id,
        CancellationToken cancellationToken)
    {
        return Results.Ok(await libraryImportService
            .QueryLibraryTargetCandidatesAsync(id, cancellationToken)
            .ConfigureAwait(false));
    }

    [MagpieCommand(
        "content-associate",
        "POST",
        "content/{id:guid}/associate",
        "Associate discovered files with a library item or its descendants")]
    internal async Task<IResult> AssociateContentLibraryImportCandidatesAsync(
        [FromServices] ILibraryImportService libraryImportService,
        AuthenticatedUser user,
        Guid id,
        [FromBody] BulkAction<LibraryImportCandidateQuery> action,
        CancellationToken cancellationToken)
    {
        if (action.Filter is not null)
        {
            return Results.Problem("Select the files to associate by id.", statusCode: StatusCodes.Status400BadRequest);
        }

        var candidates = await libraryImportService
            .QueryLibraryTargetCandidatesAsync(id, cancellationToken)
            .ConfigureAwait(false);
        var candidateIds = candidates
            .Select(candidate => candidate.Id)
            .Where(candidateId => action.Ids is null || action.Ids.Contains(candidateId))
            .ToList();
        var result = await libraryImportService
            .AssociateAsync(candidateIds, user.Id, correlationId: null, cancellationToken)
            .ConfigureAwait(false);
        return Results.Json(result, statusCode: StatusCodes.Status202Accepted);
    }

    [MagpieCommand("candidates", "GET", "candidates", "List library import candidates")]
    internal async Task<IResult> QueryLibraryImportCandidatesAsync(
        [FromServices] ILibraryImportService libraryImportService,
        Guid? rootFolderId,
        Guid? scanRunId,
        string[]? status,
        bool? hasMatches,
        string? groupKey,
        int? pageNumber,
        int? pageSize,
        string? sortBy,
        SortDirection? sortDirection,
        CancellationToken cancellationToken)
    {
        if (!TryParseStatuses(status, out var statuses))
        {
            return Results.Problem(
                "A status filter value is not a known status.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var query = CreateCandidateQuery(rootFolderId, scanRunId, statuses, hasMatches, groupKey);

        var paging = new PagingParameter { PageNumber = pageNumber ?? 1, PageSize = pageSize ?? 25 };

        var sort = !string.IsNullOrEmpty(sortBy)
            ? new SortParameter { PropertyName = sortBy, Direction = sortDirection ?? SortDirection.Ascending }
            : null;

        var result = await libraryImportService.QueryCandidatesAsync(query, paging, sort, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    [MagpieCommand(
        "candidate-groups",
        "GET",
        "candidate-groups",
        "List library import candidates, collapsed into top-level groups")]
    internal async Task<IResult> QueryLibraryImportCandidateGroupsAsync(
        [FromServices] ILibraryImportService libraryImportService,
        Guid? rootFolderId,
        Guid? scanRunId,
        string[]? status,
        bool? hasMatches,
        int? pageNumber,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        if (!TryParseStatuses(status, out var statuses))
        {
            return Results.Problem(
                "A status filter value is not a known status.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var query = CreateCandidateQuery(rootFolderId, scanRunId, statuses, hasMatches, groupKey: null);

        var paging = new PagingParameter { PageNumber = pageNumber ?? 1, PageSize = pageSize ?? 25 };

        var result = await libraryImportService.QueryCandidateGroupsAsync(query, paging, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static LibraryImportCandidateQuery CreateCandidateQuery(
        Guid? rootFolderId,
        Guid? scanRunId,
        LibraryImportCandidateStatus[]? statuses,
        bool? hasMatches,
        string? groupKey)
    {
        return new()
        {
            RootFolderId = rootFolderId,
            ScanRunId = scanRunId,
            Statuses = statuses,
            HasMatches = hasMatches,
            GroupKey = groupKey,
        };
    }

    private static bool TryParseStatuses(string[]? status, out LibraryImportCandidateStatus[]? statuses)
    {
        statuses = null;
        if (status is not { Length: > 0 })
        {
            return true;
        }

        var parsedStatuses = new List<LibraryImportCandidateStatus>();
        foreach (var value in status)
        {
            if (!Enum.TryParse<LibraryImportCandidateStatus>(value, ignoreCase: true, out var parsed))
            {
                return false;
            }

            parsedStatuses.Add(parsed);
        }

        statuses = parsedStatuses.Distinct().ToArray();
        return true;
    }

    [MagpieCommand(
        "bulk-accept",
        "POST",
        "candidates/bulk-accept",
        "Queue multiple library import candidates for import")]
    internal async Task<IResult> BulkAcceptLibraryImportCandidatesAsync(
        [FromServices] ILibraryImportService libraryImportService,
        AuthenticatedUser user,
        [FromBody] LibraryImportSubmissionRequest action,
        CancellationToken cancellationToken)
    {
        if (action.MonitoringMode is not { } monitoringMode)
        {
            return Results.Problem("A monitoring mode is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await libraryImportService
            .SubmitImportAsync(action, monitoringMode, user.Id, correlationId: null, cancellationToken)
            .ConfigureAwait(false);

        return Results.Json(result, statusCode: StatusCodes.Status202Accepted);
    }

    [MagpieCommand(
        "candidate-groups-accept",
        "POST",
        "candidate-groups/accept",
        "Queue every candidate in a library import group for import")]
    internal async Task<IResult> AcceptLibraryImportCandidateGroupAsync(
        [FromServices] ILibraryImportService libraryImportService,
        AuthenticatedUser user,
        [FromQuery] string groupKey,
        [FromQuery] Guid? rootFolderId,
        [FromQuery] Guid? scanRunId,
        [FromQuery] string[]? status,
        [FromQuery] bool? hasMatches,
        [FromQuery] LibraryImportMonitoringMode? monitoringMode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(groupKey))
        {
            return Results.Problem("A group key is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (monitoringMode is null)
        {
            return Results.Problem("A monitoring mode is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!TryParseStatuses(status, out var statuses))
        {
            return Results.Problem(
                "A status filter value is not a known status.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var query = CreateCandidateQuery(rootFolderId, scanRunId, statuses, hasMatches, groupKey);

        var result = await libraryImportService
            .AcceptGroupAsync(query, groupKey, monitoringMode.Value, user.Id, correlationId: null, cancellationToken)
            .ConfigureAwait(false);

        return result is null
            ? Results.NotFound()
            : Results.Json(result, statusCode: StatusCodes.Status202Accepted);
    }

    [MagpieCommand(
        "candidate-groups-bulk-accept",
        "POST",
        "candidate-groups/bulk-accept",
        "Queue every candidate across every matching library import group for import")]
    internal async Task<IResult> BulkAcceptLibraryImportCandidateGroupsAsync(
        [FromServices] ILibraryImportService libraryImportService,
        AuthenticatedUser user,
        [FromBody] LibraryImportSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.MonitoringMode is not { } monitoringMode)
        {
            return Results.Problem("A monitoring mode is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await libraryImportService
            .AcceptGroupsAsync(request, monitoringMode, user.Id, correlationId: null, cancellationToken)
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

    [MagpieCommand(
        "candidate-groups-match",
        "POST",
        "candidate-groups/match",
        "Apply a match to every candidate in a library import group")]
    internal async Task<IResult> SetLibraryImportCandidateGroupMatchAsync(
        [FromServices] ILibraryImportService libraryImportService,
        AuthenticatedUser user,
        [FromQuery] string groupKey,
        [FromQuery] Guid? rootFolderId,
        [FromQuery] Guid? scanRunId,
        [FromQuery] string[]? status,
        [FromQuery] bool? hasMatches,
        [FromBody] SetLibraryImportMatchRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(groupKey))
        {
            return Results.Problem("A group key is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!TryParseStatuses(status, out var statuses))
        {
            return Results.Problem(
                "A status filter value is not a known status.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var query = CreateCandidateQuery(rootFolderId, scanRunId, statuses, hasMatches, groupKey);

        var updated = await libraryImportService
            .MatchGroupAsync(
                query,
                groupKey,
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
