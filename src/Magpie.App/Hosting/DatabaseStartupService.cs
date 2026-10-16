using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Content;
using Magpie.Abstractions.Database;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Events;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Magpie.App.Hosting;

internal sealed class DatabaseStartupService(
    IServiceScopeFactory scopeFactory,
    LoadedExtensionSet loadedExtensions,
    DatabaseMigrationState migrationState,
    MagpieHostEnvironment hostEnvironment,
    IEventBus eventBus,
    ILogger<DatabaseStartupService> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var databaseManagement = scope.ServiceProvider.GetRequiredService<IDatabaseManagementProvider>();
            if (!await IsSchemaKnownAsync(databaseManagement, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            var context = scope.ServiceProvider.GetRequiredService<MagpieDbContext>();
            migrationState.RecordPendingMigrations(
                [.. await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)]);
            if (!await MigrateAsync(context.GetService<IMigrator>(), databaseManagement, cancellationToken)
                .ConfigureAwait(false))
            {
                return;
            }

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

        await DispatchLifecycleEventsAsync(scopeFactory, loadedExtensions.ActivationGeneration, cancellationToken)
            .ConfigureAwait(false);
    }

    internal async Task<bool> MigrateAsync(
        IMigrator migrator,
        IDatabaseManagementProvider databaseManagement,
        CancellationToken cancellationToken)
    {
        var applied = await databaseManagement.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false);
        await migrator.MigrateAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var appliedAfter = await databaseManagement.GetAppliedMigrationsAsync(cancellationToken)
            .ConfigureAwait(false);

        if (appliedAfter.Count > applied.Count)
        {
            await eventBus.PublishAsync(
                new MagpieEvent(
                    Guid.Empty,
                    MagpieEvents.Names.DatabaseMigrated,
                    new Dictionary<string, string>(),
                    default,
                    default,
                    null),
                cancellationToken).ConfigureAwait(false);
        }

        if (!await IsSchemaKnownAsync(databaseManagement, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        migrationState.RecordMigrated();
        return true;
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
        long generation,
        CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();

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

    private async Task<bool> IsSchemaKnownAsync(
        IDatabaseManagementProvider databaseManagement,
        CancellationToken cancellationToken)
    {
        var unknownMigrations = await databaseManagement.GetUnknownMigrationsAsync(cancellationToken)
            .ConfigureAwait(false);
        migrationState.RecordSchemaCheck(unknownMigrations);
        if (unknownMigrations.Count > 0)
        {
            logger.LogError(
                "The database schema is newer than node {NodeId}. Unknown migrations: {Migrations}",
                hostEnvironment.NodeId,
                string.Join(", ", unknownMigrations));
        }

        return unknownMigrations.Count == 0;
    }
}
