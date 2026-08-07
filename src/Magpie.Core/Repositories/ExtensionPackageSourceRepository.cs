using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
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

internal sealed class ExtensionPackageSourceRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    ILogger<ExtensionPackageSourceRepository> logger,
    IDataProtectionService dataProtectionService,
    TimeProvider? timeProvider = null) : IExtensionPackageSourceRepository
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    private const string DefaultTagFilter = "magpie";

    private static void Normalize(ExtensionPackageSource model)
    {
        model.Name = model.Name.Trim();
        model.SourceUrl = model.SourceUrl.Trim();

        if (model.SearchFilterKind == ExtensionPackageSourceSearchFilterKind.Tag &&
            string.IsNullOrWhiteSpace(model.SearchFilterValue))
        {
            model.SearchFilterValue = DefaultTagFilter;
        }
        else if (model.SearchFilterKind == ExtensionPackageSourceSearchFilterKind.None)
        {
            model.SearchFilterValue = null;
        }
        else
        {
            model.SearchFilterValue = string.IsNullOrWhiteSpace(model.SearchFilterValue)
                ? null
                : model.SearchFilterValue.Trim();
        }

        model.Username = string.IsNullOrWhiteSpace(model.Username) ? null : model.Username.Trim();
        model.SecretReference = string.IsNullOrWhiteSpace(model.SecretReference) ? null : model.SecretReference.Trim();
        model.EncryptedSecret = string.IsNullOrWhiteSpace(model.EncryptedSecret) ? null : model.EncryptedSecret;

        if (model.CredentialKind == ExtensionPackageSourceCredentialKind.None)
        {
            model.Username = null;
            model.EncryptedSecret = null;
            model.SecretReference = null;
        }
        else if (model.CredentialKind == ExtensionPackageSourceCredentialKind.Basic)
        {
            model.SecretReference = null;
        }
        else if (model.CredentialKind == ExtensionPackageSourceCredentialKind.SecretReference)
        {
            model.EncryptedSecret = null;
        }
    }

    private static IReadOnlyCollection<string> ChangedFields(ExtensionPackageSourcePatch patch)
    {
        var fields = new List<string>();
        if (patch.Name is not null)
        {
            fields.Add("name");
        }

        if (patch.SourceUrl is not null)
        {
            fields.Add("sourceUrl");
        }

        if (patch.Enabled is not null)
        {
            fields.Add("enabled");
        }

        if (patch.SearchFilterKind is not null)
        {
            fields.Add("searchFilterKind");
        }

        if (patch.SearchFilterValue is not null)
        {
            fields.Add("searchFilterValue");
        }

        if (patch.CredentialKind is not null)
        {
            fields.Add("credentialKind");
        }

        if (patch.Username is not null)
        {
            fields.Add("username");
        }

        if (patch.EncryptedSecret is not null)
        {
            fields.Add("encryptedSecret");
        }

        if (patch.SecretReference is not null)
        {
            fields.Add("secretReference");
        }

        return fields;
    }

    public async Task<IEnumerable<ExtensionPackageSource>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var sources = await context.ExtensionPackageSources
                .AsNoTracking()
                .OrderBy(s => s.Name)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            Unprotect(sources);
            return sources;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The package source repository failed to list the package sources.");
            throw;
        }
    }

    public async Task<PagedList<ExtensionPackageSource>> QueryAsync(
        ExtensionPackageSourceQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var filtered = context.ExtensionPackageSources.AsNoTracking();
            if (query.Enabled is { } enabled)
            {
                filtered = filtered.Where(source => source.Enabled == enabled);
            }

            var totalItems = await filtered.CountAsync(cancellationToken).ConfigureAwait(false);
            var ordered = sort?.Direction == SortDirection.Descending
                ? filtered.OrderByDescending(source => source.Name).ThenBy(source => source.Id)
                : filtered.OrderBy(source => source.Name).ThenBy(source => source.Id);
            IQueryable<ExtensionPackageSource> results = ordered;
            results = results
                .Skip((paging.PageNumber - 1) * paging.PageSize)
                .Take(paging.PageSize);

            var sources = await results.ToListAsync(cancellationToken).ConfigureAwait(false);

            Unprotect(sources);
            return new()
            {
                Items = sources,
                PageNumber = paging.PageNumber,
                PageSize = paging.PageSize,
                TotalItems = totalItems
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The package source repository failed to query the package sources.");
            throw;
        }
    }

    public async Task<ExtensionPackageSource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var source = await context.ExtensionPackageSources
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (source is not null)
            {
                Unprotect(source);
            }

            return source;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The package source repository failed to read package source {PackageSourceId}.", id);
            throw;
        }
    }

    public async Task<ExtensionPackageSource> CreateAsync(
        ExtensionPackageSource model,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Normalize(model);

            var created = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    if (model.Id == Guid.Empty)
                    {
                        model.Id = Guid.NewGuid();
                    }

                    model.CreatedAt = now;
                    model.CreatedByUserId = createdByUserId;
                    model.ModifiedAt = null;
                    model.ModifiedByUserId = null;

                    var plaintextSecret = model.EncryptedSecret;
                    model.EncryptedSecret = Protect(plaintextSecret);

                    context.ExtensionPackageSources.Add(model);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    context.Entry(model).State = EntityState.Detached;
                    model.EncryptedSecret = plaintextSecret;

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.System,
                            Operation = "Create",
                            EntityId = model.Id.ToString(),
                            EntityType = nameof(ExtensionPackageSource),
                            Detail = $"Created extension package source {model.Name}",
                            Timestamp = now,
                            UserId = createdByUserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    return model;
                },
                cancellationToken).ConfigureAwait(false);

            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(
                    ExtensionEventBuilder.BuildExtensionPackageSourceCreatedEvent(created.Id),
                    token),
                cancellationToken).ConfigureAwait(false);

            return created;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The package source repository failed to create package source " +
                "{PackageSourceName}.", model.Name);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(
        ExtensionPackageSource model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Normalize(model);

            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var encryptedSecret = Protect(model.EncryptedSecret);

                    var updated = await context.ExtensionPackageSources
                        .Where(s => s.Id == model.Id)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(p => p.Name, model.Name)
                                .SetProperty(p => p.SourceUrl, model.SourceUrl)
                                .SetProperty(p => p.Enabled, model.Enabled)
                                .SetProperty(p => p.SearchFilterKind, model.SearchFilterKind)
                                .SetProperty(p => p.SearchFilterValue, model.SearchFilterValue)
                                .SetProperty(p => p.CredentialKind, model.CredentialKind)
                                .SetProperty(p => p.Username, model.Username)
                                .SetProperty(p => p.EncryptedSecret, encryptedSecret)
                                .SetProperty(p => p.SecretReference, model.SecretReference)
                                .SetProperty(p => p.ModifiedAt, now)
                                .SetProperty(p => p.ModifiedByUserId, modifiedByUserId),
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
                                EntityType = nameof(ExtensionPackageSource),
                                Detail = $"Updated extension package source {model.Name}",
                                Timestamp = now,
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
            logger.LogError(ex, "The package source repository failed to update package source " +
                "{PackageSourceId}.", model.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken = default)
    {
        try
        {
            var removed = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var model = await (
                        from s in context.ExtensionPackageSources.AsNoTracking()
                        where s.Id == id
                        select new { s.Name })
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    var deleted = await context.ExtensionPackageSources
                        .Where(s => s.Id == id)
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (deleted > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.System,
                                Operation = "Delete",
                                EntityId = id.ToString(),
                                EntityType = nameof(ExtensionPackageSource),
                                Detail = $"Deleted extension package source {model.Name}",
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
                        ExtensionEventBuilder.BuildExtensionPackageSourceDeletedEvent(id),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return removed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The package source repository failed to delete package source {PackageSourceId}.", id);
            throw;
        }
    }

    public async Task<bool> PatchAsync(
        ExtensionPackageSourcePatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var patched = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var model = await context.ExtensionPackageSources
                        .FirstOrDefaultAsync(s => s.Id == patch.Id, cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    var now = _time.GetUtcNow().UtcDateTime;
                    if (patch.Name is not null)
                    {
                        model.Name = patch.Name.Value;
                    }

                    if (patch.SourceUrl is not null)
                    {
                        model.SourceUrl = patch.SourceUrl.Value;
                    }

                    if (patch.Enabled is not null)
                    {
                        model.Enabled = patch.Enabled.Value;
                    }

                    if (patch.SearchFilterKind is not null)
                    {
                        model.SearchFilterKind = patch.SearchFilterKind.Value;
                    }

                    if (patch.SearchFilterValue is not null)
                    {
                        model.SearchFilterValue = patch.SearchFilterValue.Value;
                    }

                    if (patch.CredentialKind is not null)
                    {
                        model.CredentialKind = patch.CredentialKind.Value;
                    }

                    if (patch.Username is not null)
                    {
                        model.Username = patch.Username.Value;
                    }

                    if (patch.EncryptedSecret is not null)
                    {
                        model.EncryptedSecret = patch.EncryptedSecret.Value;
                    }

                    if (patch.SecretReference is not null)
                    {
                        model.SecretReference = patch.SecretReference.Value;
                    }

                    Normalize(model);

                    if (patch.EncryptedSecret is not null)
                    {
                        model.EncryptedSecret = Protect(model.EncryptedSecret);
                    }

                    model.ModifiedAt = now;
                    model.ModifiedByUserId = modifiedByUserId;

                    var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    if (updated > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.System,
                                Operation = "Patch",
                                EntityId = patch.Id.ToString(),
                                EntityType = nameof(ExtensionPackageSource),
                                Detail = $"Updated extension package source {model.Name}.",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (patched)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        ExtensionEventBuilder.BuildExtensionPackageSourceUpdatedEvent(patch.Id, ChangedFields(patch)),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return patched;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The package source repository failed to patch package source " +
                "{PackageSourceId}.", patch.Id);
            throw;
        }
    }

    private void Unprotect(IEnumerable<ExtensionPackageSource> sources)
    {
        foreach (var source in sources)
        {
            Unprotect(source);
        }
    }

    private void Unprotect(ExtensionPackageSource source)
    {
        source.EncryptedSecret = string.IsNullOrWhiteSpace(source.EncryptedSecret)
            ? null
            : dataProtectionService.Unprotect(source.EncryptedSecret);
    }

    private string? Protect(string? secret) => string.IsNullOrWhiteSpace(secret)
        ? null
        : dataProtectionService.Protect(secret);
}
