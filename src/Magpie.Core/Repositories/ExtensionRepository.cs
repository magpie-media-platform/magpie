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
using Magpie.Sdk.Extensions;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class ExtensionRepository(
    MagpieDbContext context,
    ITransactionManager transactionManager,
    IAuditRecordRepository auditRecordRepository,
    IEventBus eventBus,
    ILogger<ExtensionRepository> logger,
    TimeProvider timeProvider) : IExtensionRepository
{
    private const int ActivationStateId = 1;

    private static readonly (string Name, Func<Extension, Extension, bool> Changed)[] UpdateFields =
    [
        (nameof(Extension.PackageId), (current, model) => !Equals(current.PackageId, model.PackageId)),
        (nameof(Extension.Version), (current, model) => !Equals(current.Version, model.Version)),
        (nameof(Extension.LifecycleVersion), (current, model) =>
            !Equals(current.LifecycleVersion, model.LifecycleVersion)),
        (nameof(Extension.Name), (current, model) => !Equals(current.Name, model.Name)),
        (nameof(Extension.Author), (current, model) => !Equals(current.Author, model.Author)),
        (nameof(Extension.Description), (current, model) => !Equals(current.Description, model.Description)),
        (nameof(Extension.TargetABI), (current, model) => !Equals(current.TargetABI, model.TargetABI)),
        (nameof(Extension.FilePath), (current, model) => !Equals(current.FilePath, model.FilePath)),
        (nameof(Extension.AssemblyName), (current, model) => !Equals(current.AssemblyName, model.AssemblyName)),
        (nameof(Extension.Installed), (current, model) => !Equals(current.Installed, model.Installed)),
        (nameof(Extension.Enabled), (current, model) => !Equals(current.Enabled, model.Enabled)),
        (nameof(Extension.DisabledReason), (current, model) => !Equals(current.DisabledReason, model.DisabledReason)),
        (nameof(Extension.LoadOrder), (current, model) => !Equals(current.LoadOrder, model.LoadOrder)),
        (nameof(Extension.Managed), (current, model) => !Equals(current.Managed, model.Managed)),
        (nameof(Extension.NuGetId), (current, model) => !Equals(current.NuGetId, model.NuGetId)),
        (nameof(Extension.PackageRepository), (current, model) =>
            !Equals(current.PackageRepository, model.PackageRepository)),
        (nameof(Extension.AutoUpgrade), (current, model) => !Equals(current.AutoUpgrade, model.AutoUpgrade)),
        (nameof(Extension.LastChecked), (current, model) => !Equals(current.LastChecked, model.LastChecked)),
    ];

    private readonly TimeProvider _time = timeProvider;

    private static IReadOnlyCollection<string> ChangedFields(ExtensionPatch patch)
    {
        var fields = new List<string>();
        if (patch.NuGetId is not null)
        {
            fields.Add(nameof(Extension.NuGetId));
        }

        if (patch.PackageRepository is not null)
        {
            fields.Add(nameof(Extension.PackageRepository));
        }

        if (patch.AutoUpgrade is not null)
        {
            fields.Add(nameof(Extension.AutoUpgrade));
        }

        if (patch.LoadOrder is not null)
        {
            fields.Add(nameof(Extension.LoadOrder));
        }

        if (patch.Enabled is not null)
        {
            fields.Add(nameof(Extension.Enabled));
        }

        if (patch.DisabledReason is not null)
        {
            fields.Add(nameof(Extension.DisabledReason));
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
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    EnsurePackageId(model);

                    model.CreatedAt = _time.GetUtcNow().UtcDateTime;
                    model.CreatedByUserId = createdByUserId;

                    context.Extensions.Add(model);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.System,
                            Operation = "Create",
                            EntityId = model.Id.ToString(),
                            EntityType = nameof(Extension),
                            Detail = $"Created extension {model.Name}",
                            Timestamp = _time.GetUtcNow().UtcDateTime,
                            UserId = createdByUserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    return model;
                },
                cancellationToken).ConfigureAwait(false);
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
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    EnsurePackageId(model);

                    var now = _time.GetUtcNow().UtcDateTime;

                    var current = await context.Extensions.AsNoTracking()
                        .FirstOrDefaultAsync(entity => entity.Id == model.Id, cancellationToken)
                        .ConfigureAwait(false);
                    if (current is null)
                    {
                        return false;
                    }

                    var changedFields = new List<string>();
                    foreach (var (name, changed) in UpdateFields)
                    {
                        if (changed(current, model))
                        {
                            changedFields.Add(name);
                        }
                    }

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

                    if (updated > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.System,
                                Operation = "Update",
                                EntityId = model.Id.ToString(),
                                EntityType = nameof(Extension),
                                Detail = $"Updated extension {model.Name}",
                                Timestamp = _time.GetUtcNow().UtcDateTime,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    if (updated > 0 && changedFields.Count > 0)
                    {
                        await transactionManager.OnCommittedAsync(
                            token => eventBus.PublishAsync(
                                ExtensionEventBuilder.BuildExtensionUpdatedEvent(model.Id, changedFields),
                                token),
                            cancellationToken).ConfigureAwait(false);
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);
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
                    var extensionName = await context.Extensions.AsNoTracking()
                        .Where(e => e.Id == id)
                        .Select(e => e.Name)
                        .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                    var groups = await context.SettingsGroups.AsNoTracking()
                        .Where(group => group.ExtensionId == id)
                        .Include(group => group.Settings)
                        .ToListAsync(cancellationToken).ConfigureAwait(false);
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

                    if (deleted > 0)
                    {
                        foreach (var group in groups)
                        {
                            foreach (var setting in group.Settings)
                            {
                                await auditRecordRepository.CreateAsync(
                                    new()
                                    {
                                        Category = AuditCategories.System,
                                        Operation = "Delete",
                                        EntityId = setting.Id.ToString(),
                                        ParentEntityId = group.Id.ToString(),
                                        EntityType = nameof(Setting),
                                        Detail = $"Deleted setting {setting.Key} from group {group.Name}",
                                        UserId = deletedByUserId
                                    },
                                    cancellationToken).ConfigureAwait(false);
                            }

                            await auditRecordRepository.CreateAsync(
                                new()
                                {
                                    Category = AuditCategories.System,
                                    Operation = "Delete",
                                    EntityId = group.Id.ToString(),
                                    ParentEntityId = id.ToString(),
                                    EntityType = nameof(SettingsGroup),
                                    Detail = $"Deleted settings group {group.Name}",
                                    UserId = deletedByUserId
                                },
                                cancellationToken).ConfigureAwait(false);
                        }

                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.System,
                                Operation = "Delete",
                                EntityId = id.ToString(),
                                EntityType = nameof(Extension),
                                Detail = $"Deleted extension {extensionName}",
                                UserId = deletedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

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

    public async Task RegisterLoadedExtensionsAsync(
        IReadOnlyList<Extension> discovered,
        IReadOnlyList<IExtension> loaded,
        IReadOnlyDictionary<Guid, string> packageVersions,
        CancellationToken cancellationToken)
    {
        try
        {
            await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    await context.ExtensionActivationStates
                        .Where(state => state.Id == ActivationStateId)
                        .ExecuteUpdateAsync(
                            state => state.SetProperty(e => e.Generation, e => e.Generation),
                            cancellationToken)
                        .ConfigureAwait(false);

                    var extensions = discovered.Count > 0
                        ? discovered
                        : await context.Extensions.ToListAsync(cancellationToken).ConfigureAwait(false);

                    foreach (var extension in extensions)
                    {
                        var match = loaded.FirstOrDefault(candidate =>
                            string.Equals(
                                candidate.GetType().Assembly.GetName().Name,
                                extension.AssemblyName,
                                StringComparison.OrdinalIgnoreCase));

                        if (match is null)
                        {
                            continue;
                        }

                        extension.Name = match.Name;
                        extension.Version = packageVersions.GetValueOrDefault(extension.Id) ?? match.Version.ToString();
                        extension.TargetABI = match.TargetABI;
                    }

                    var registeredIds = await context.Extensions
                        .Select(e => new { e.PackageId, e.Id })
                        .ToDictionaryAsync(e => e.PackageId, e => e.Id, cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var extension in discovered.Where(e => registeredIds.ContainsKey(e.PackageId)))
                    {
                        extension.Id = registeredIds[extension.PackageId];
                    }

                    context.Extensions.AddRange(discovered.Where(e => !registeredIds.ContainsKey(e.PackageId)));
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The extension repository failed to register the loaded extensions.");
            throw;
        }
    }

    public async Task<bool> TrySetLifecycleVersionAsync(
        Guid id,
        string? expectedVersion,
        string? version,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var extensions = context.Extensions.Where(e => e.Id == id);
            extensions = string.IsNullOrEmpty(expectedVersion)
                ? extensions.Where(e => e.LifecycleVersion == null || e.LifecycleVersion == "")
                : extensions.Where(e => e.LifecycleVersion == expectedVersion);

            return await extensions
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.LifecycleVersion, version), cancellationToken)
                .ConfigureAwait(false) == 1;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The extension repository failed to set the lifecycle version of extension {ExtensionId}.",
                id);
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
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
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

                    if (patch.Enabled is not null)
                    {
                        model.Enabled = patch.Enabled.Value;
                    }

                    if (patch.DisabledReason is not null)
                    {
                        model.DisabledReason = patch.DisabledReason.Value;
                    }

                    model.ModifiedAt = _time.GetUtcNow().UtcDateTime;
                    model.ModifiedByUserId = modifiedByUserId;

                    var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    var changedFields = ChangedFields(patch);
                    if (updated > 0 && changedFields.Count > 0)
                    {
                        await transactionManager.OnCommittedAsync(
                            token => eventBus.PublishAsync(
                                ExtensionEventBuilder.BuildExtensionUpdatedEvent(patch.Id, changedFields),
                                token),
                            cancellationToken).ConfigureAwait(false);
                    }

                    if (updated > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.System,
                                Operation = "Patch",
                                EntityId = patch.Id.ToString(),
                                EntityType = nameof(Extension),
                                Detail = $"Patched extension {model.Name}",
                                Timestamp = _time.GetUtcNow().UtcDateTime,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The extension repository failed to patch extension {ExtensionId}.", patch.Id);
            throw;
        }
    }
}
