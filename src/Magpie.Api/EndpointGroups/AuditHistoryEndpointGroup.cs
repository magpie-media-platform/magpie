using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class AuditHistoryEndpointGroup : IApiEndpointGroup
{
    public string Name => "audit-history";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetAuditHistoryAsync)
            .WithName(nameof(GetAuditHistoryAsync))
            .Produces<PagedList<AuditHistoryEntry>>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireViewer();

        builder.MapPost("purge", PurgeAuditHistoryAsync)
            .WithName(nameof(PurgeAuditHistoryAsync))
            .Produces<AuditHistoryPurgeCounts>()
            .RequireAdmin();
    }

    internal async Task<IResult> GetAuditHistoryAsync(
        [FromServices] IAuditHistoryService auditHistoryService,
        AuthenticatedUser user,
        Guid? contentId,
        Guid? jobExecutionId,
        AuditHistoryEntryKind? kind,
        string? status,
        DateTime? from,
        DateTime? to,
        string[]? category,
        Guid? userId,
        bool? hasUser,
        Guid? correlationId,
        string? search,
        SortDirection? sortDirection,
        int? pageNumber,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        if (contentId is null && jobExecutionId is null && !user.IsInRole(Role.Admin))
        {
            return Results.Forbid();
        }

        var query = new AuditHistoryQuery
        {
            Paging = new() { PageNumber = pageNumber ?? 1, PageSize = pageSize ?? 25 },
            ContentId = contentId,
            JobExecutionId = jobExecutionId,
            Kind = kind,
            Status = status,
            From = from,
            To = to,
            Categories = category,
            UserId = userId,
            HasUser = hasUser,
            CorrelationId = correlationId,
            Search = search,
            Direction = sortDirection ?? SortDirection.Descending
        };

        var result = await auditHistoryService.QueryAsync(query, cancellationToken).ConfigureAwait(false);

        return Results.Ok(result);
    }

    internal async Task<IResult> PurgeAuditHistoryAsync(
        [FromServices] IAuditHistoryService auditHistoryService,
        AuthenticatedUser user,
        int olderThanDays,
        CancellationToken cancellationToken)
    {
        var counts = await auditHistoryService.PurgeAsync(olderThanDays, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(counts);
    }
}
