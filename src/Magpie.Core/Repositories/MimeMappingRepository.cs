using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Data;
using Magpie.Models.Data;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class MimeMappingRepository(
    MagpieDbContext context,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    ILogger<MimeMappingRepository> logger) : IMimeMappingRepository
{
    public async Task<IEnumerable<MimeMapping>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.MimeMappings
                .AsNoTracking()
                .OrderBy(m => m.Extension)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The MIME mapping repository failed to list MIME mappings.");
            throw;
        }
    }

    public async Task<MimeMapping?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.MimeMappings
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Extension == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The MIME mapping repository failed to read MIME mapping {Extension}.", id);
            throw;
        }
    }

    public async Task<MimeMapping> CreateAsync(
        MimeMapping model,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            context.MimeMappings.Add(model);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(
                    MimeMappingEventBuilder.BuildMimeMappingCreatedEvent(model.Extension), token),
                cancellationToken).ConfigureAwait(false);

            return model;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The MIME mapping repository failed to create MIME mapping " +
                "{Extension}.", model.Extension);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(
        MimeMapping model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var updated = await context.MimeMappings
                .Where(m => m.Extension == model.Extension)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(m => m.MimeType, model.MimeType)
                        .SetProperty(m => m.UpdatedAt, model.UpdatedAt),
                    cancellationToken)
                .ConfigureAwait(false);

            if (updated > 0)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        MimeMappingEventBuilder.BuildMimeMappingUpdatedEvent(model.Extension, ["mimeType"]), token),
                    cancellationToken).ConfigureAwait(false);
            }

            return updated > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The MIME mapping repository failed to update MIME mapping " +
                "{Extension}.", model.Extension);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(string id, Guid? deletedByUserId, CancellationToken cancellationToken = default)
    {
        try
        {
            var deleted = await context.MimeMappings
                .Where(m => m.Extension == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(MimeMappingEventBuilder.BuildMimeMappingDeletedEvent(id), token),
                    cancellationToken).ConfigureAwait(false);
            }

            return deleted > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The MIME mapping repository failed to delete MIME mapping {Extension}.", id);
            throw;
        }
    }
}
