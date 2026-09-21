using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Content;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Magpie.App.Hosting;

internal sealed class DatabaseStartupService(
    IServiceScopeFactory scopeFactory,
    LoadedExtensionSet loadedExtensions,
    ILogger<DatabaseStartupService> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var context = scope.ServiceProvider.GetRequiredService<MagpieDbContext>();
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

            if (loadedExtensions.Discovered.Count > 0)
            {
                await RegisterLoadedExtensionsAsync(
                    context,
                    loadedExtensions.Discovered,
                    loadedExtensions.Loaded,
                    logger,
                    cancellationToken).ConfigureAwait(false);
            }

            await BackfillMediaFilePathKeysAsync(scope.ServiceProvider, context, cancellationToken)
                .ConfigureAwait(false);
            await ProtectStoredSettingsAsync(
                context,
                scope.ServiceProvider.GetRequiredService<IDataProtectionService>(),
                cancellationToken).ConfigureAwait(false);
        }

        await DispatchLifecycleEventsAsync(scopeFactory, cancellationToken).ConfigureAwait(false);
    }

    private static async Task BackfillMediaFilePathKeysAsync(
        IServiceProvider scopedServices,
        MagpieDbContext context,
        CancellationToken cancellationToken)
    {
        if (!await context.MediaFiles.AsNoTracking()
            .AnyAsync(mediaFile => mediaFile.PathKey.StartsWith("legacy:"), cancellationToken)
            .ConfigureAwait(false))
        {
            return;
        }

        await NormalizeMediaFilePathKeysAsync(
            scopedServices.GetRequiredService<IMediaFileRepository>(),
            scopedServices.GetRequiredService<IContentRepository>(),
            scopedServices.GetRequiredService<IContentFileService>(),
            scopedServices.GetRequiredService<IContentService>(),
            scopedServices.GetRequiredService<ITransactionManager>(),
            scopedServices.GetRequiredService<IAuditRecordRepository>(),
            scopedServices.GetRequiredService<TimeProvider>(),
            cancellationToken).ConfigureAwait(false);
    }

    private static string ToMediaFilePathKey(string filePath) =>
        OperatingSystem.IsWindows() ? filePath.ToUpperInvariant() : filePath;

    internal static async Task NormalizeMediaFilePathKeysAsync(
        IMediaFileRepository repository,
        IContentRepository contentRepository,
        IContentFileService files,
        IContentService content,
        ITransactionManager transactions,
        IAuditRecordRepository audit,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default)
    {
        await transactions.ExecuteWithTransactionAsync(async ct =>
        {
            var mediaFiles = await repository.ListAllAsync(ct).ConfigureAwait(false);
            foreach (var group in mediaFiles.GroupBy(
                file => ToMediaFilePathKey(file.FilePath), StringComparer.Ordinal))
            {
                var ordered = group.OrderBy(file => file.PathKey.StartsWith("legacy:", StringComparison.Ordinal))
                    .ThenBy(file => file.CreatedAt).ThenBy(file => file.Id).ToArray();
                var survivor = ordered[0];
                foreach (var duplicate in ordered.Skip(1))
                {
                    var item = await contentRepository.GetByIdAsync(duplicate.ContentItemId, ct)
                        .ConfigureAwait(false)
                        ?? throw new InvalidOperationException("The media file has no content item.");
                    await files.UnlinkAsync(item.ContentHierarchyItemId, duplicate.Id, null, ct).ConfigureAwait(false);
                    await content.MaterializeFulfillmentStatusAsync(
                        item.ContentHierarchyItemId, null, includeSelf: false, ct).ConfigureAwait(false);
                }

                if (survivor.PathKey == group.Key)
                {
                    continue;
                }

                await repository.UpdateAsync(survivor, survivor.ModifiedByUserId, ct).ConfigureAwait(false);
                await audit.CreateAsync(new()
                {
                    Category = AuditCategories.Content,
                    Operation = "FilePathNormalized",
                    EntityType = "MediaFile",
                    EntityId = survivor.Id.ToString(),
                    ParentEntityId = survivor.ContentItemId.ToString(),
                    Detail = $"Normalized the path key for {survivor.FilePath}.",
                    Timestamp = timeProvider.GetUtcNow().UtcDateTime
                }, ct).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task ProtectStoredSettingsAsync(
        MagpieDbContext context,
        IDataProtectionService protector,
        CancellationToken cancellationToken)
    {
        var protectedRows = 0;
        var servers = await context.MediaServers.ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var server in servers)
        {
            if (server.Settings.Count == 0 || protector.TryGetKeyId(server.Settings, out _))
            {
                continue;
            }

            server.Settings = protector.Protect(server.Settings);
            context.Entry(server).Property(entity => entity.Settings).IsModified = true;
            protectedRows++;
        }

        var rules = await context.NotificationRules.ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var rule in rules)
        {
            if (rule.Settings.Count == 0 || protector.TryGetKeyId(rule.Settings, out _))
            {
                continue;
            }

            rule.Settings = protector.Protect(rule.Settings);
            context.Entry(rule).Property(entity => entity.Settings).IsModified = true;
            protectedRows++;
        }

        if (protectedRows > 0)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task DispatchLifecycleEventsAsync(
        IServiceScopeFactory scopeFactory,
        CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var activationGenerationRepository = scope.ServiceProvider
            .GetRequiredService<IExtensionActivationGenerationRepository>();
        var generation = await activationGenerationRepository.GetCurrentAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await scope.ServiceProvider.GetRequiredService<IExtensionLifecycleManager>()
                .DispatchStartupHooksAsync(cancellationToken).ConfigureAwait(false);
            await RecordExtensionNodeStatusAsync(
                scope.ServiceProvider,
                generation,
                ExtensionNodeHealth.CurrentHealthy,
                "Extension activation succeeded.",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RecordExtensionNodeStatusAsync(
                scope.ServiceProvider,
                generation,
                ExtensionNodeHealth.ActivationFailed,
                ex.Message,
                cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task RecordExtensionNodeStatusAsync(
        IServiceProvider serviceProvider,
        long appliedGeneration,
        ExtensionNodeHealth health,
        string message,
        CancellationToken cancellationToken)
    {
        var hostEnvironment = serviceProvider.GetRequiredService<MagpieHostEnvironment>();
        var repository = serviceProvider.GetRequiredService<IExtensionNodeStatusRepository>();
        var timeProvider = serviceProvider.GetRequiredService<TimeProvider>();
        await repository.UpsertAsync(
            new ExtensionNodeStatus
            {
                NodeId = hostEnvironment.NodeId,
                AppliedGeneration = appliedGeneration,
                ExtensionHealth = health,
                SchedulerEligible = !hostEnvironment.IsClustered || health is ExtensionNodeHealth.CurrentHealthy,
                LastSeenAt = timeProvider.GetUtcNow().UtcDateTime,
                Message = message,
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task RegisterLoadedExtensionsAsync(
        MagpieDbContext context,
        IReadOnlyList<Extension> extensions,
        IReadOnlyList<IExtension> loaded,
        ILogger logger,
        CancellationToken cancellationToken)
    {
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
            extension.Version = match.Version.ToString();
            extension.TargetABI = match.TargetABI;
        }

        context.Extensions.AddRange(extensions);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("The host registered {ExtensionCount} bundled extensions on " +
            "first start", extensions.Count);
    }
}
