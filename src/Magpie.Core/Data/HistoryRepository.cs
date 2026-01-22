using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Data;
using Magpie.Data.Database;
using Magpie.Models;
using Magpie.Models.History;
using Magpie.Models.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Data;

/// <summary>
/// Repository for recording and querying history entries.
/// </summary>
public sealed class HistoryRepository(MagpieDbContext context, ILogger<HistoryRepository> logger) : IHistoryRepository
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<HistoryEntry>>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entities = await context.HistoryEntries.ToListAsync(cancellationToken).ConfigureAwait(false);
            var models = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<HistoryEntry>>(models);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting all history entries ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<HistoryEntry>>(
                "Error searching history",
                ErrorCodes.DbHistoryError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<HistoryEntry>> CreateAsync(HistoryEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            var entity = entry.CreateEntity();
            context.HistoryEntries.Add(entity);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Audit:{AuditCategory}:{AuditOperation}:{EntityType}({ParentEntityId}/{EntityId}) " +
                "by UserId {UserId}: {AuditDescription}",
                entry.Category,
                entry.Operation,
                entry.EntityType ?? "",
                entry.ParentEntityId ?? "",
                entry.EntityId ?? "",
                entry.UserId?.ToString() ?? "System",
                entry.Description ?? "");

            return Result.Success(entity.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error creating history entry {AuditCategory}/{AuditOperation} by user {UserId} ({ExErrorCode})",
                entry.Category,
                entry.Operation,
                entry.UserId,
                errorCode ?? "");
            return Result.Fail<HistoryEntry>("Error creating history entry", ErrorCodes.DbHistoryError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<HistoryEntry>>> SearchAsync(
        HistoryQuery queryCriteria,
        PagingRequest? paging,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = context.HistoryEntries.AsQueryable();

            if (queryCriteria.EntityId is not null)
            {
                query = query.Where(e => e.EntityId == queryCriteria.EntityId);
            }
            if (queryCriteria.ParentEntityId is not null)
            {
                query = query.Where(e => e.ParentEntityId == queryCriteria.ParentEntityId);
            }
            if (queryCriteria.EntityType is not null)
            {
                query = query.Where(e => e.EntityType == queryCriteria.EntityType);
            }
            if (queryCriteria.Category is not null)
            {
                query = query.Where(e => e.Category == queryCriteria.Category);
            }
            if (queryCriteria.Operation is not null)
            {
                query = query.Where(e => e.Operation == queryCriteria.Operation);
            }
            if (queryCriteria.Success is not null)
            {
                query = query.Where(e => e.Success == queryCriteria.Success);
            }
            if (queryCriteria.Result is not null)
            {
                query = query.Where(e => e.Result == queryCriteria.Result);
            }
            if (queryCriteria.DescriptionSearch is not null)
            {
                query = query
                    .Where(e => e.Description != null && e.Description.Contains(queryCriteria.DescriptionSearch));
            }
            if (queryCriteria.TimestampStart is not null)
            {
                query = query.Where(e => e.Timestamp >= queryCriteria.TimestampStart);
            }
            if (queryCriteria.TimestampEnd is not null)
            {
                query = query.Where(e => e.Timestamp <= queryCriteria.TimestampEnd);
            }
            if (queryCriteria.UserId is not null)
            {
                query = query.Where(e => e.UserId == queryCriteria.UserId);
            }
            query = query.OrderByDescending(e => e.Timestamp);

            var totalItems = await query.CountAsync(cancellationToken).ConfigureAwait(false);

            if (paging is not null)
            {
                query = query.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);
            }

            var queryResult = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
            var results = queryResult.Select(e => e.ToModel()).ToImmutableList();
            var resultPage = new PagedResult<HistoryEntry>(results, paging?.PageNumber, paging?.PageSize, totalItems);

            return Result.Success(resultPage);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error searching history {Filter} ({ExErrorCode})", queryCriteria, errorCode ?? "");
            return Result.Fail<PagedResult<HistoryEntry>>(
                "Error searching history",
                ErrorCodes.DbHistoryError,
                errorCode);
        }
    }
}
