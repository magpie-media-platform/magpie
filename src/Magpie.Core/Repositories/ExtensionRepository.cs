using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Data;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Events;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class ExtensionRepository(
    MagpieDbContext context,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    ILogger<ExtensionRepository> logger,
    TimeProvider? timeProvider = null) : IExtensionRepository
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    private static IReadOnlyCollection<string> ChangedFields(ExtensionPatch patch)
    {
        var fields = new List<string>();
        if (patch.NuGetId is not null)
        {
            fields.Add("nuGetId");
        }

        if (patch.PackageRepository is not null)
        {
            fields.Add("packageRepository");
        }

        if (patch.AutoUpgrade is not null)
        {
            fields.Add("autoUpgrade");
        }

        if (patch.LoadOrder is not null)
        {
            fields.Add("loadOrder");
        }

        return fields;
    }

    private static void EnsurePackageId(Extension model)
    {
        if (!string.IsNullOrWhiteSpace(model.PackageId))
        {
            return;
        }

        model.PackageId = !string.IsNullOrWhiteSpace(model.NuGetId)
            ? model.NuGetId
            : model.AssemblyName;
    }

    public async Task<IEnumerable<Extension>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.Extensions
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The extension repository failed to list the extensions.");
            throw;
        }
    }

    public async Task<Extension?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.Extensions
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The extension repository failed to read extension {ExtensionId}.", id);
            throw;
        }
    }

    public async Task<PagedList<Extension>> QueryAsync(
        ExtensionQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var filtered = context.Extensions.AsNoTracking();
            if (query.PackageId is not null)
            {
                filtered = filtered.Where(extension => extension.PackageId == query.PackageId);
            }

            var totalItems = await filtered.CountAsync(cancellationToken).ConfigureAwait(false);
            var ordered = sort?.Direction == SortDirection.Descending
                ? filtered.OrderByDescending(extension => extension.PackageId).ThenBy(extension => extension.Id)
                : filtered.OrderBy(extension => extension.PackageId).ThenBy(extension => extension.Id);
            IQueryable<Extension> results = ordered;
            results = results
                .Skip((paging.PageNumber - 1) * paging.PageSize)
                .Take(paging.PageSize);

            return new()
            {
                Items = await results.ToListAsync(cancellationToken).ConfigureAwait(false),
                PageNumber = paging.PageNumber,
                PageSize = paging.PageSize,
                TotalItems = totalItems
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The extension repository failed to query extensions.");
            throw;
        }
    }

    public async Task<Extension> CreateAsync(
        Extension model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            EnsurePackageId(model);

            model.CreatedAt = _time.GetUtcNow().UtcDateTime;
            model.CreatedByUserId = createdByUserId;

            context.Extensions.Add(model);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return model;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The extension repository failed to create extension {ExtensionName}.", model.Name);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(Extension model, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            EnsurePackageId(model);

            var now = _time.GetUtcNow().UtcDateTime;

            var updated = await context.Extensions
                .Where(e => e.Id == model.Id)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(e => e.PackageId, model.PackageId)
                        .SetProperty(e => e.Version, model.Version)
                        .SetProperty(e => e.LifecycleVersion, model.LifecycleVersion)
                        .SetProperty(e => e.Name, model.Name)
                        .SetProperty(e => e.Author, model.Author)
                        .SetProperty(e => e.Description, model.Description)
                        .SetProperty(e => e.TargetABI, model.TargetABI)
                        .SetProperty(e => e.FilePath, model.FilePath)
                        .SetProperty(e => e.AssemblyName, model.AssemblyName)
                        .SetProperty(e => e.Installed, model.Installed)
                        .SetProperty(e => e.Enabled, model.Enabled)
                        .SetProperty(e => e.DisabledReason, model.DisabledReason)
                        .SetProperty(e => e.LoadOrder, model.LoadOrder)
                        .SetProperty(e => e.Managed, model.Managed)
                        .SetProperty(e => e.NuGetId, model.NuGetId)
                        .SetProperty(e => e.PackageRepository, model.PackageRepository)
                        .SetProperty(e => e.AutoUpgrade, model.AutoUpgrade)
                        .SetProperty(e => e.LastChecked, model.LastChecked)
                        .SetProperty(e => e.ModifiedAt, now)
                        .SetProperty(e => e.ModifiedByUserId, modifiedByUserId),
                    cancellationToken)
                .ConfigureAwait(false);

            return updated > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The extension repository failed to update extension {ExtensionId}.", model.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    await context.Settings
                        .Where(setting => context.SettingsGroups
                            .Where(group => group.ExtensionId == id)
                            .Select(group => group.Id)
                            .Contains(setting.SettingsGroupId))
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);
                    await context.SettingsGroups
                        .Where(group => group.ExtensionId == id)
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);

                    var deleted = await context.Extensions
                        .Where(e => e.Id == id)
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);

                    return deleted > 0;
                },
                cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The extension repository failed to delete extension {ExtensionId}.", id);
            throw;
        }
    }

    public async Task<bool> PatchAsync(
        ExtensionPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var model = await context.Extensions
                .FirstOrDefaultAsync(e => e.Id == patch.Id, cancellationToken)
                .ConfigureAwait(false);

            if (model is null)
            {
                return false;
            }

            if (patch.NuGetId is not null)
            {
                model.NuGetId = patch.NuGetId.Value;
            }

            if (patch.PackageRepository is not null)
            {
                model.PackageRepository = patch.PackageRepository.Value;
            }

            if (patch.AutoUpgrade is not null)
            {
                model.AutoUpgrade = patch.AutoUpgrade.Value;
            }

            if (patch.LoadOrder is not null)
            {
                model.LoadOrder = patch.LoadOrder.Value;
            }

            model.ModifiedAt = _time.GetUtcNow().UtcDateTime;
            model.ModifiedByUserId = modifiedByUserId;

            var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            var changedFields = ChangedFields(patch);
            if (updated > 0 && changedFields.Count > 0)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(ExtensionUpdated(patch.Id, changedFields), token),
                    cancellationToken).ConfigureAwait(false);
            }

            return updated > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The extension repository failed to patch extension {ExtensionId}.", patch.Id);
            throw;
        }
    }

    private static Models.MagpieEvent ExtensionUpdated(Guid extensionId, IReadOnlyCollection<string> changedFields)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.ExtensionId] = extensionId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.ExtensionChangedFields] = string.Join(",", changedFields ?? []),
        };

        return new(Guid.Empty, MagpieEvents.Names.ExtensionUpdated, context, default, default, null);
    }
}
