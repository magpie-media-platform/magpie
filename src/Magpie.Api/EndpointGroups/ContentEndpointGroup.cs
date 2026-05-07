using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.QueryLanguage;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class ContentEndpointGroup(
    IContentHierarchyItemRepository contentHierarchyItemRepository,
    IJobManager jobManager) : IApiEndpointGroup
{
    public string Name => "content";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", QueryContent)
            .WithName(nameof(QueryContent))
            .Produces<PagedList<ContentHierarchyItem>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireViewer();

        builder.MapGet("stats", GetContentStats)
            .WithName(nameof(GetContentStats))
            .Produces<ContentStats>()
            .RequireViewer();

        builder.MapGet("{id:guid}", GetContentById)
            .WithName(nameof(GetContentById))
            .Produces<ContentHierarchyItem>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapGet("{id:guid}/children", GetContentChildren)
            .WithName(nameof(GetContentChildren))
            .Produces<PagedList<ContentHierarchyItem>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPatch("{id:guid}", PatchContent)
            .WithName(nameof(PatchContent))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireRequester();

        builder.MapDelete("{id:guid}", DeleteContent)
            .WithName(nameof(DeleteContent))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapPost("{id:guid}/refresh", RefreshContent)
            .WithName(nameof(RefreshContent))
            .Produces<Guid>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRequester();

        builder.MapPost("{id:guid}/search", SearchContent)
            .WithName(nameof(SearchContent))
            .Produces<Guid>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRequester();
    }

    private async Task<IResult> QueryContent(
        string? filter,
        string? contentType,
        string? level,
        Guid? parentId,
        Guid? rootFolderId,
        MonitoringIntent? monitoring,
        string? search,
        string? tag,
        int? page,
        int? pageSize,
        string? sortBy,
        SortDirection? sortDirection,
        CancellationToken cancellationToken)
    {
        var criteria = new ContentHierarchyItemCriteria();

        if (!string.IsNullOrWhiteSpace(filter))
        {
            var parseResult = FilterQuery.ParseContentFilter(filter);
            if (!parseResult.IsSuccess)
            {
                return Results.BadRequest(new { message = parseResult.Error!.Value.Message });
            }

            criteria = parseResult.Value!.ContentHierarchyItem?.And?.FirstOrDefault()
                ?? criteria;
        }

        if (contentType is not null)
        {
            criteria = criteria with
            {
                ContentType = new() { And = [new() { Value = contentType }] }
            };
        }

        if (level is not null)
        {
            criteria = criteria with
            {
                Level = new() { And = [new() { Value = level }] }
            };
        }

        if (parentId is not null)
        {
            criteria = criteria with
            {
                ParentId = new() { And = [new() { Value = parentId.Value }] }
            };
        }

        if (rootFolderId is not null)
        {
            criteria = criteria with
            {
                RootFolderId = new() { And = [new() { Value = rootFolderId.Value }] }
            };
        }

        if (monitoring is not null)
        {
            criteria = criteria with
            {
                Monitoring = new() { And = [new() { Value = monitoring.Value }] }
            };
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            criteria = criteria with
            {
                Name = new()
                {
                    And = [new() { Operator = CriteriaValueOperator.ContainsString, Value = search }]
                }
            };
        }

        if (tag is not null)
        {
            criteria = criteria with
            {
                Tags = new() { And = [new() { Value = tag }] }
            };
        }

        var paging = new PagingParameter
        {
            PageNumber = page ?? 1,
            PageSize = pageSize ?? 25
        };

        SortParameter? sort = sortBy is not null
            ? new SortParameter
            {
                PropertyName = sortBy,
                Direction = sortDirection ?? SortDirection.Ascending
            }
            : null;

        var result = await contentHierarchyItemRepository.QueryAsync(criteria, paging, sort, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private async Task<IResult> GetContentStats(CancellationToken cancellationToken)
    {
        var stats = await contentHierarchyItemRepository.GetStatsAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(stats);
    }

    private async Task<IResult> GetContentById(Guid id, CancellationToken cancellationToken)
    {
        var item = await contentHierarchyItemRepository.GetByIdAsync(id, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(item);
    }

    private async Task<IResult> GetContentChildren(
        Guid id,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var parent = await contentHierarchyItemRepository.GetByIdAsync(id, cancellationToken)
            .ConfigureAwait(false);

        if (parent is null)
        {
            return Results.NotFound();
        }

        var criteria = new ContentHierarchyItemCriteria
        {
            ParentId = new() { And = [new() { Value = id }] }
        };

        var paging = new PagingParameter
        {
            PageNumber = page ?? 1,
            PageSize = pageSize ?? 25
        };

        var result = await contentHierarchyItemRepository.QueryAsync(criteria, paging, null, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private async Task<IResult> PatchContent(
        HttpContext httpContext,
        Guid id,
        ContentHierarchyItemPatch request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var patch = request with { Id = id };

        var patched = await contentHierarchyItemRepository.PatchAsync(patch, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        if (!patched)
        {
            return Results.NotFound();
        }

        return Results.NoContent();
    }

    private async Task<IResult> DeleteContent(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var deleted = await contentHierarchyItemRepository.DeleteAsync(id, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        if (!deleted)
        {
            return Results.NotFound();
        }

        return Results.NoContent();
    }

    private async Task<IResult> RefreshContent(HttpContext httpContext, Guid id, CancellationToken cancellationToken)
    {
        var item = await contentHierarchyItemRepository.GetByIdAsync(id, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return Results.NotFound();
        }

        var userId = httpContext.User.GetUserId();
        var executionId = await jobManager.ExecuteAsync(
            "RefreshMetadata",
            null,
            new Dictionary<string, string> { ["contentHierarchyItemId"] = id.ToString() },
            JobExecutionMode.Queue,
            userId,
            cancellationToken).ConfigureAwait(false);

        return Results.Accepted($"api/{Name}/{id}/refresh", executionId);
    }

    private async Task<IResult> SearchContent(HttpContext httpContext, Guid id, CancellationToken cancellationToken)
    {
        var item = await contentHierarchyItemRepository.GetByIdAsync(id, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return Results.NotFound();
        }

        var userId = httpContext.User.GetUserId();
        var executionId = await jobManager.ExecuteAsync(
            "ReleaseSearch",
            null,
            new Dictionary<string, string> { ["contentHierarchyItemId"] = id.ToString() },
            JobExecutionMode.Queue,
            userId,
            cancellationToken).ConfigureAwait(false);

        return Results.Accepted($"api/{Name}/{id}/search", executionId);
    }
}
