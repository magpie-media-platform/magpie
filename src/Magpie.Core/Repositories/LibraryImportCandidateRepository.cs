using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class LibraryImportCandidateRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    ILogger<LibraryImportCandidateRepository> logger,
    TimeProvider? timeProvider = null) : ILibraryImportCandidateRepository
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<IEnumerable<LibraryImportCandidate>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.LibraryImportCandidates
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The library import repository failed to list library import matches.");
            throw;
        }
    }

    public async Task<LibraryImportCandidate?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.LibraryImportCandidates
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The library import repository failed to read library import candidate " +
                "{LibraryImportCandidateId}.", id);
            throw;
        }
    }

    public async Task<PagedList<LibraryImportCandidate>> QueryAsync(
        LibraryImportCandidateQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken)
    {
        try
        {
            var matches = context.LibraryImportCandidates.AsNoTracking().AsQueryable();

            if (query.RootFolderId is not null)
            {
                matches = matches.Where(m => m.RootFolderId == query.RootFolderId);
            }

            if (query.ScanRunId is not null)
            {
                matches = matches.Where(m => m.ScanRunId == query.ScanRunId);
            }

            if (query.Status is not null)
            {
                matches = matches.Where(m => m.Status == query.Status);
            }

            if (query.Statuses is { Count: > 0 })
            {
                var statuses = query.Statuses as LibraryImportCandidateStatus[] ?? [.. query.Statuses];
                matches = matches.Where(m => statuses.Contains(m.Status));
            }

            if (query.HasMatches is not null)
            {
                matches = query.HasMatches.Value
                    ? matches.Where(m => m.MatchCount > 0)
                    : matches.Where(m => m.MatchCount == 0);
            }

            if (query.ExcludeIds is { Count: > 0 })
            {
                var excludeIds = query.ExcludeIds as Guid[] ?? [.. query.ExcludeIds];
                matches = matches.Where(m => !excludeIds.Contains(m.Id));
            }

            var totalCount = await matches.CountAsync(cancellationToken).ConfigureAwait(false);

            var sortProperty = typeof(LibraryImportCandidate).GetProperties()
                .Select(property => property.Name)
                .FirstOrDefault(name => string.Equals(name, sort?.PropertyName, StringComparison.OrdinalIgnoreCase));
            if (sort is not null && sortProperty is not null)
            {
                matches = sort.Direction == SortDirection.Ascending
                    ? matches.OrderBy(m => EF.Property<LibraryImportCandidate>(m, sortProperty)).ThenBy(m => m.Id)
                    : matches.OrderByDescending(m => EF.Property<LibraryImportCandidate>(m, sortProperty))
                        .ThenBy(m => m.Id);
            }
            else
            {
                matches = matches.OrderBy(m => m.Id);
            }

            matches = matches.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);

            var items = await matches.ToListAsync(cancellationToken).ConfigureAwait(false);

            return new()
            {
                Items = items,
                PageNumber = paging.PageNumber,
                PageSize = paging.PageSize,
                TotalItems = totalCount
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The library import repository failed to query library import matches.");
            throw;
        }
    }

    public async Task<bool> TryClaimForImportAsync(
        Guid candidateId,
        Guid? claimedByUserId,
        CancellationToken cancellationToken)
    {
        return await TransitionStatusAsync(
            candidateId,
            LibraryImportCandidateStatus.Pending,
            LibraryImportCandidateStatus.Queued,
            note: null,
            claimedByUserId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> TryBeginImportAsync(Guid candidateId, Guid? userId, CancellationToken cancellationToken)
    {
        return await TransitionStatusAsync(
            candidateId,
            LibraryImportCandidateStatus.Queued,
            LibraryImportCandidateStatus.Importing,
            note: null,
            userId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> MarkImportFailedAsync(
        Guid candidateId,
        string reason,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var affected = await context.LibraryImportCandidates
                .Where(m => m.Id == candidateId &&
                    (m.Status == LibraryImportCandidateStatus.Queued ||
                        m.Status == LibraryImportCandidateStatus.Importing))
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(m => m.Status, LibraryImportCandidateStatus.Failed)
                        .SetProperty(m => m.Notes, reason)
                        .SetProperty(m => m.ModifiedAt, now)
                        .SetProperty(m => m.ModifiedByUserId, userId),
                    cancellationToken)
                .ConfigureAwait(false);

            if (affected > 0)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        ContentEventBuilder.BuildLibraryImportCandidateUpdatedEvent(candidateId),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return affected > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The library import repository failed to mark library import candidate {LibraryImportCandidateId} " +
                "failed.",
                candidateId);
            throw;
        }
    }

    private async Task<bool> TransitionStatusAsync(
        Guid candidateId,
        LibraryImportCandidateStatus expected,
        LibraryImportCandidateStatus next,
        string? note,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var affected = await context.LibraryImportCandidates
                .Where(m => m.Id == candidateId && m.Status == expected)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(m => m.Status, next)
                        .SetProperty(m => m.Notes, m => note ?? m.Notes)
                        .SetProperty(m => m.ModifiedAt, now)
                        .SetProperty(m => m.ModifiedByUserId, userId),
                    cancellationToken)
                .ConfigureAwait(false);

            if (affected > 0)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        ContentEventBuilder.BuildLibraryImportCandidateUpdatedEvent(candidateId),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return affected > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The library import repository failed to change library import candidate {LibraryImportCandidateId} " +
                "from {From} to {To}.",
                candidateId,
                expected,
                next);
            throw;
        }
    }

    public async Task<int> DeleteByRootFolderAsync(
        Guid rootFolderId,
        Guid? deletedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;

                    var deleted = await context.LibraryImportCandidates
                        .Where(m => m.RootFolderId == rootFolderId &&
                            m.Status != LibraryImportCandidateStatus.Queued &&
                            m.Status != LibraryImportCandidateStatus.Importing)
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (deleted > 0)
                    {
                        var importCandidateCountLabel = deleted == 1
                            ? "1 import candidate"
                            : $"{deleted} import candidates";
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Content,
                                Operation = "Delete",
                                EntityId = rootFolderId.ToString(),
                                EntityType = nameof(LibraryImportCandidate),
                                Detail = $"Deleted {importCandidateCountLabel} for root folder {rootFolderId}.",
                                Timestamp = now,
                                UserId = deletedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return deleted;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The library import repository failed to delete library import matches for root " +
                "folder {RootFolderId}.", rootFolderId);
            throw;
        }
    }

    public async Task<LibraryImportCandidate> CreateAsync(
        LibraryImportCandidate model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    model.CreatedAt = now;
                    model.CreatedByUserId = createdByUserId;

                    model.MatchCount = model.Matches.Count;

                    context.LibraryImportCandidates.Add(model);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Content,
                            Operation = "Create",
                            EntityId = model.Id.ToString(),
                            EntityType = nameof(LibraryImportCandidate),
                            Detail = $"Created import candidate for {model.FilePath}.",
                            Timestamp = now,
                            UserId = createdByUserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    return model;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The library import repository failed to create library import candidate for " +
                "{FilePath}.", model.FilePath);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(
        LibraryImportCandidate model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;

                    var affected = await context.LibraryImportCandidates
                        .Where(m => m.Id == model.Id)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(m => m.RootFolderId, model.RootFolderId)
                                .SetProperty(m => m.ScanRunId, model.ScanRunId)
                                .SetProperty(m => m.FilePath, model.FilePath)
                                .SetProperty(m => m.ContentType, model.ContentType)
                                .SetProperty(m => m.Status, model.Status)
                                .SetProperty(m => m.ParsedSignals, model.ParsedSignals)
                                .SetProperty(m => m.Matches, model.Matches)
                                .SetProperty(m => m.MatchCount, model.Matches.Count)
                                .SetProperty(m => m.Notes, model.Notes)
                                .SetProperty(m => m.ModifiedAt, now)
                                .SetProperty(m => m.ModifiedByUserId, modifiedByUserId),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (affected > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Content,
                                Operation = "Update",
                                EntityId = model.Id.ToString(),
                                EntityType = nameof(LibraryImportCandidate),
                                Detail = $"Updated import candidate for {model.FilePath}.",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return affected > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (updated)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        ContentEventBuilder.BuildLibraryImportCandidateUpdatedEvent(model.Id),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return updated;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The library import repository failed to update library import candidate " +
                "{LibraryImportCandidateId}.", model.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            var removed = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;

                    var model = await (
                        from m in context.LibraryImportCandidates.AsNoTracking()
                        where m.Id == id
                        select new { m.FilePath })
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    var deleted = await context.LibraryImportCandidates
                        .Where(m => m.Id == id)
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (deleted > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Content,
                                Operation = "Delete",
                                EntityId = id.ToString(),
                                EntityType = nameof(LibraryImportCandidate),
                                Detail = $"Deleted import candidate for {model.FilePath}.",
                                Timestamp = now,
                                UserId = deletedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return deleted > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (removed)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        ContentEventBuilder.BuildLibraryImportCandidateUpdatedEvent(id),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return removed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The library import repository failed to delete library import candidate " +
                "{LibraryImportCandidateId}.", id);
            throw;
        }
    }
}
