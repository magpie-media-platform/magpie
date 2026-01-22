using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.History;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository for recording and querying history entries.
/// </summary>
public interface IHistoryRepository : IRepository<HistoryEntry>
{
    /// <summary>
    /// Create a new <see cref="HistoryEntry"/> entity.
    /// </summary>
    /// <param name="model">The history entry model to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<HistoryEntry>> CreateAsync(HistoryEntry model, CancellationToken cancellationToken = default);

    /// <summary>
    /// Search history entries with filtering and optional pagination.
    /// </summary>
    /// <param name="query">The history query criteria.</param>
    /// <param name="paging">Pagination parameters, or null for all results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<PagedResult<HistoryEntry>>> SearchAsync(
        HistoryQuery query,
        PagingRequest? paging,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Record a new history entry.
    /// </summary>
    /// <param name="category"><inheritdoc cref="HistoryEntry.Category"  path="/summary"/></param>
    /// <param name="operation"><inheritdoc cref="HistoryEntry.Operation"  path="/summary"/></param>
    /// <param name="success"><inheritdoc cref="HistoryEntry.Success"  path="/summary"/></param>
    /// <param name="entityId"><inheritdoc cref="HistoryEntry.EntityId"  path="/summary"/></param>
    /// <param name="parentEntityId"><inheritdoc cref="HistoryEntry.ParentEntityId"  path="/summary"/></param>
    /// <param name="entityType"><inheritdoc cref="HistoryEntry.EntityType"  path="/summary"/></param>
    /// <param name="description"><inheritdoc cref="HistoryEntry.Description"  path="/summary"/></param>
    /// <param name="result"><inheritdoc cref="HistoryEntry.Result"  path="/summary"/></param>
    /// <param name="detail"><inheritdoc cref="HistoryEntry.Detail"  path="/summary"/></param>
    /// <param name="userId"><inheritdoc cref="HistoryEntry.UserId"  path="/summary"/></param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<HistoryEntry>> RecordEntryAsync(
        string category,
        string operation,
        object? entityId = null,
        object? parentEntityId = null,
        string? entityType = null,
        string? description = null,
        Guid? userId = null,
        bool success = true,
        string? result = null,
        string? detail = null,
        CancellationToken cancellationToken = default)
    {
        return CreateAsync(new HistoryEntry(
             entityId?.ToString(),
             parentEntityId?.ToString(),
             entityType,
             category,
             operation,
             description,
             success,
             result,
             detail,
             DateTimeOffset.UtcNow,
             userId
        ), cancellationToken);
    }
}
