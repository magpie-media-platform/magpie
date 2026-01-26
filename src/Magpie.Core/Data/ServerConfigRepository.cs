using System;
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
/// Repository for managing server configuration.
/// </summary>
public sealed class ServerConfigRepository(
    MagpieDbContext context,
    IHistoryRepository historyRepository,
    ILogger<ServerConfigRepository> logger) : IServerConfigRepository
{
    private const int SingletonId = 1;

    /// <inheritdoc />
    public async Task<Result<ServerConfig?>> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from sc in context.ServerConfigs.AsNoTracking()
                where sc.Id == SingletonId
                select sc)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(entity?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting server config ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<ServerConfig?>(
                "Error retrieving server config",
                ErrorCodes.DbServerConfigError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateAsync(
        ServerConfig model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var updated = await context.ServerConfigs
                .Where(sc => sc.Id == SingletonId)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.DefaultLanguagePreference, model.DefaultLanguagePreference)
                        .SetProperty(e => e.AutoUpdateEnabled, model.AutoUpdateEnabled)
                        .SetProperty(e => e.BackupSchedule, model.BackupSchedule)
                        .SetProperty(e => e.BackupRetentionPeriod, model.BackupRetentionPeriod),
                    cancellationToken)
                .ConfigureAwait(false);

            if (updated > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Update",
                    entityId: SingletonId,
                    entityType: nameof(ServerConfig),
                    description: "Updated server configuration",
                    userId: modifiedByUserId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error updating server config ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<bool>(
                "Error updating server config",
                ErrorCodes.DbServerConfigError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> PatchAsync(
        ServerConfigPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = await context.ServerConfigs
                .FirstOrDefaultAsync(sc => sc.Id == SingletonId, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            if (patch.DefaultLanguagePreference is not null)
            {
                entity.DefaultLanguagePreference = patch.DefaultLanguagePreference.Value;
            }

            if (patch.AutoUpdateEnabled is not null)
            {
                entity.AutoUpdateEnabled = patch.AutoUpdateEnabled.Value;
            }

            if (patch.BackupSchedule is not null)
            {
                entity.BackupSchedule = patch.BackupSchedule.Value!;
            }

            if (patch.BackupRetentionPeriod is not null)
            {
                entity.BackupRetentionPeriod = patch.BackupRetentionPeriod.Value;
            }

            var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (updated > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Patch",
                    entityId: SingletonId,
                    entityType: nameof(ServerConfig),
                    description: "Patched server configuration",
                    userId: modifiedByUserId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error patching server config ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<bool>(
                "Error patching server config",
                ErrorCodes.DbServerConfigError,
                errorCode);
        }
    }
}
