using System;
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

            await scope.ServiceProvider.GetRequiredService<IExtensionRepository>()
                .RegisterLoadedExtensionsAsync(
                    loadedExtensions.Discovered,
                    loadedExtensions.Loaded,
                    loadedExtensions.PackageVersions,
                    cancellationToken)
                .ConfigureAwait(false);
            if (loadedExtensions.Discovered.Count > 0)
            {
                logger.LogInformation(
                    "Registered {ExtensionCount} bundled extensions on first start",
                    loadedExtensions.Discovered.Count);
            }

            await BackfillMediaFilePathKeysAsync(scope.ServiceProvider, context, cancellationToken)
                .ConfigureAwait(false);
            var contentRepository = scope.ServiceProvider.GetRequiredService<IContentRepository>();
            var roots = await context.ContentHierarchyItems.AsNoTracking()
                .Where(item => item.ParentId == null).Select(item => item.Id)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var root in roots)
            {
                await contentRepository.ReconcileMediaStateAsync(root, null, cancellationToken).ConfigureAwait(false);
            }

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

        await scopedServices.GetRequiredService<IContentFileService>()
            .NormalizeMediaFilePathKeysAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ProtectStoredSettingsAsync(
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
}
