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
            .RequireViewer();
    }

    internal async Task<IResult> GetAuditHistoryAsync(
        [FromServices] IAuditHistoryService auditHistoryService,
        Guid? contentId,
        Guid? jobExecutionId,
        AuditHistoryEntryKind? kind,
        string? status,
        DateTime? from,
        DateTime? to,
        int? pageNumber,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var query = new AuditHistoryQuery
        {
            Paging = new() { PageNumber = pageNumber ?? 1, PageSize = pageSize ?? 25 },
            ContentId = contentId,
            JobExecutionId = jobExecutionId,
            Kind = kind,
            Status = status,
            From = from,
            To = to
        };

        var result = await auditHistoryService.QueryAsync(query, cancellationToken).ConfigureAwait(false);

        return Results.Ok(result);
    }
}
