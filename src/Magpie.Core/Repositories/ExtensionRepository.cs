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
    private static readonly (string Name, Func<Extension, Extension, bool> Changed)[] UpdateFields =
    [
        ("packageId", (current, model) => !Equals(current.PackageId, model.PackageId)),
        ("version", (current, model) => !Equals(current.Version, model.Version)),
        ("lifecycleVersion", (current, model) => !Equals(current.LifecycleVersion, model.LifecycleVersion)),
        ("name", (current, model) => !Equals(current.Name, model.Name)),
        ("author", (current, model) => !Equals(current.Author, model.Author)),
        ("description", (current, model) => !Equals(current.Description, model.Description)),
        ("targetABI", (current, model) => !Equals(current.TargetABI, model.TargetABI)),
        ("filePath", (current, model) => !Equals(current.FilePath, model.FilePath)),
        ("assemblyName", (current, model) => !Equals(current.AssemblyName, model.AssemblyName)),
        ("installed", (current, model) => !Equals(current.Installed, model.Installed)),
        ("enabled", (current, model) => !Equals(current.Enabled, model.Enabled)),
        ("disabledReason", (current, model) => !Equals(current.DisabledReason, model.DisabledReason)),
        ("loadOrder", (current, model) => !Equals(current.LoadOrder, model.LoadOrder)),
        ("managed", (current, model) => !Equals(current.Managed, model.Managed)),
        ("nuGetId", (current, model) => !Equals(current.NuGetId, model.NuGetId)),
        ("packageRepository", (current, model) => !Equals(current.PackageRepository, model.PackageRepository)),
        ("autoUpgrade", (current, model) => !Equals(current.AutoUpgrade, model.AutoUpgrade)),
        ("lastChecked", (current, model) => !Equals(current.LastChecked, model.LastChecked)),
    ];

    private readonly TimeProvider _time = timeProvider;

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

        if (patch.Enabled is not null)
        {
            fields.Add("enabled");
        }

        if (patch.DisabledReason is not null)
        {
            fields.Add("disabledReason");
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

            context.Extensions.AddRange(discovered);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The extension repository failed to register the loaded extensions.");
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
