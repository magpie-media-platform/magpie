using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Data;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class IndexerConnectionRepository(
    MagpieDbContext context,
    ILogger<IndexerConnectionRepository> logger,
    IDataProtectionService protector) : IIndexerConnectionRepository
{
    public async Task<IEnumerable<IndexerConnection>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var connections = await context.IndexerConnections
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var connection in connections)
            {
                connection.Settings = protector.Unprotect(connection.Settings);
            }

            return connections;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The indexer connection repository failed to list the indexer connections.");
            throw;
        }
    }

    public async Task<IndexerConnection?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var connection = await context.IndexerConnections
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (connection is not null)
            {
                connection.Settings = protector.Unprotect(connection.Settings);
            }

            return connection;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The indexer connection repository failed to read indexer connection {IndexerConnectionId}.",
                id);
            throw;
        }
    }

    public async Task<PagedList<IndexerConnection>> QueryAsync(
        IndexerConnectionQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken)
    {
        try
        {
            var connections = context.IndexerConnections.AsNoTracking().AsQueryable();

            if (query.IndexerId is not null)
            {
                connections = connections.Where(c => c.IndexerId == query.IndexerId);
            }

            if (query.Tag is not null)
            {
                var taggedIds =
                    from t in context.Tags.AsNoTracking()
                    where t.Name == query.Tag
                    from c in t.IndexerConnections
                    select c.Id;
                connections = connections.Where(c => taggedIds.Contains(c.Id));
            }

            var totalCount = await connections.CountAsync(cancellationToken).ConfigureAwait(false);

            var sortableProperties = new[]
            {
                nameof(IndexerConnection.Id), nameof(IndexerConnection.IndexerId), nameof(IndexerConnection.Name),
                nameof(IndexerConnection.Enabled),
                nameof(IndexerConnection.Status),
                nameof(IndexerConnection.LastChecked),
                nameof(IndexerConnection.LastError),
                nameof(IndexerConnection.ConsecutiveFailures),
                nameof(IndexerConnection.CreatedAt),
                nameof(IndexerConnection.CreatedByUserId),
                nameof(IndexerConnection.ModifiedAt),
                nameof(IndexerConnection.ModifiedByUserId),
            };
            var sortProperty = sortableProperties
                .FirstOrDefault(name => string.Equals(name, sort?.PropertyName, StringComparison.OrdinalIgnoreCase));
            if (sort is not null && sortProperty is not null)
            {
                connections = sort.Direction == SortDirection.Ascending
                    ? connections
                        .OrderBy(c => EF.Property<object>(c, sortProperty))
                        .ThenBy(c => c.Id)
                    : connections
                        .OrderByDescending(c => EF.Property<object>(c, sortProperty))
                        .ThenBy(c => c.Id);
            }
            else
            {
                connections = connections.OrderBy(c => c.Name).ThenBy(c => c.Id);
            }

            connections = connections.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);

            var items = await connections.ToListAsync(cancellationToken).ConfigureAwait(false);

            foreach (var connection in items)
            {
                connection.Settings = protector.Unprotect(connection.Settings);
            }

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
            logger.LogError(ex, "The indexer connection repository failed to query the indexer connections.");
            throw;
        }
    }
}
