using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Data;
using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class ExtensionNodeStatusRepository(
    MagpieDbContext context,
    ILogger<ExtensionNodeStatusRepository> logger) : IExtensionNodeStatusRepository
{
    public async Task<ExtensionNodeStatus?> GetByNodeIdAsync(
        string nodeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.ExtensionNodeStatuses
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.NodeId == nodeId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The node status repository failed to read the status of node {NodeId}.", nodeId);
            throw;
        }
    }

    public async Task<ExtensionNodeStatus> UpsertAsync(
        ExtensionNodeStatus status,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await context.ExtensionNodeStatuses
                .FirstOrDefaultAsync(e => e.NodeId == status.NodeId, cancellationToken)
                .ConfigureAwait(false);

            if (existing is null)
            {
                context.ExtensionNodeStatuses.Add(status);
            }
            else
            {
                existing.AppliedGeneration = status.AppliedGeneration;
                existing.ExtensionHealth = status.ExtensionHealth;
                existing.SchedulerEligible = status.SchedulerEligible;
                existing.LastSeenAt = status.LastSeenAt;
                existing.Message = status.Message;
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return status;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The node status repository failed to write the status of node {NodeId}.",
                status.NodeId);
            throw;
        }
    }
}
