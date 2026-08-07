using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Models.Data;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Extensions;

internal sealed class ExtensionLifecycleManager(
    ExtensionLoadContext loadContext,
    IEventBus eventBus,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ExtensionLifecycleManager> logger) : IExtensionLifecycleManager
{
    private static bool Matches(IExtension loaded, Extension extension, IFileSystem fileSystem)
    {
        var assembly = loaded.GetType().Assembly;
        if (!string.IsNullOrWhiteSpace(extension.AssemblyName) &&
            string.Equals(assembly.GetName().Name, extension.AssemblyName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(extension.FilePath) || string.IsNullOrWhiteSpace(assembly.Location))
        {
            return false;
        }

        return fileSystem.PathsEqual(assembly.Location, extension.FilePath);
    }

    public async Task DispatchStartupHooksAsync(CancellationToken cancellationToken = default)
    {
        await DisableFailedExtensionsAsync(cancellationToken).ConfigureAwait(false);

        if (loadContext.Loaded.Count == 0)
        {
            return;
        }

        if (!loadContext.Loaded.Any(extension => extension is IExtensionLifecycle))
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var context = CreateContext(scope.ServiceProvider);
        var repository = scope.ServiceProvider.GetRequiredService<IExtensionRepository>();
        var fileSystem = scope.ServiceProvider.GetRequiredService<IFileSystem>();
        var rows = (await repository.ListAllAsync(cancellationToken).ConfigureAwait(false)).ToArray();

        foreach (var extension in loadContext.Loaded)
        {
            if (extension is not IExtensionLifecycle lifecycle)
            {
                continue;
            }

            var assembly = extension.GetType().Assembly;
            var runtimeName = assembly.GetName().Name;
            var runtimePath = assembly.Location;
            var row = extension.Id == Guid.Empty
                ? null
                : rows.FirstOrDefault(candidate => candidate.Id == extension.Id);
            if (row is null && !string.IsNullOrWhiteSpace(runtimePath))
            {
                foreach (var candidate in rows.Where(candidate => !string.IsNullOrWhiteSpace(candidate.FilePath)))
                {
                    try
                    {
                        if (fileSystem.PathsEqual(runtimePath, candidate.FilePath!))
                        {
                            row = candidate;
                            break;
                        }
                    }
                    catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                    {
                    }
                }
            }

            if (row is null && !string.IsNullOrWhiteSpace(runtimeName))
            {
                row = rows.FirstOrDefault(candidate => string.Equals(
                    candidate.PackageId,
                    runtimeName,
                    StringComparison.OrdinalIgnoreCase));
            }

            if (row is null && !string.IsNullOrWhiteSpace(runtimeName))
            {
                row = rows.FirstOrDefault(candidate => string.Equals(
                    candidate.AssemblyName,
                    runtimeName,
                    StringComparison.OrdinalIgnoreCase));
            }

            if (row is null)
            {
                logger.LogWarning(
                    "Extension {ExtensionName} has no desired extension row. " +
                    "The lifecycle manager skipped the install and upgrade hooks",
                    extension.Name);
            }
            else
            {
                var currentVersion = extension.Version.ToString();
                if (string.IsNullOrWhiteSpace(row.LifecycleVersion))
                {
                    await DispatchOnExtensionInstallAsync(
                        lifecycle, extension, row, repository, context, cancellationToken).ConfigureAwait(false);
                }
                else if (!string.Equals(row.LifecycleVersion, currentVersion, StringComparison.Ordinal))
                {
                    await DispatchOnExtensionUpgradeAsync(
                        lifecycle, extension, row, repository, context, cancellationToken).ConfigureAwait(false);
                }
            }

            await TryInvokeAsync(
                () => lifecycle.OnAppInitializeAsync(context, cancellationToken),
                extension.Name,
                nameof(IExtensionLifecycle.OnAppInitializeAsync)).ConfigureAwait(false);
        }
    }

    public Task InvokeDisableAsync(Extension extension, CancellationToken cancellationToken = default)
    {
        return InvokeHookAsync(
            extension,
            (lifecycle, context) => lifecycle.OnExtensionDisableAsync(context, cancellationToken),
            nameof(IExtensionLifecycle.OnExtensionDisableAsync));
    }

    public Task InvokeUninstallAsync(Extension extension, CancellationToken cancellationToken = default)
    {
        return InvokeHookAsync(
            extension,
            (lifecycle, context) => lifecycle.OnExtensionUninstallAsync(context, cancellationToken),
            nameof(IExtensionLifecycle.OnExtensionUninstallAsync));
    }

    private static ExtensionLifecycleContext CreateContext(IServiceProvider scopedServices)
    {
        return new ExtensionLifecycleContext(
            scopedServices.GetRequiredService<IMagpieSystem>().NodeId,
            scopedServices,
            scopedServices.GetRequiredService<ILoggerFactory>());
    }

    private async Task InvokeHookAsync(
        Extension extension,
        Func<IExtensionLifecycle, ExtensionLifecycleContext, Task> hook,
        string hookName)
    {
        using var scope = scopeFactory.CreateScope();
        var fileSystem = scope.ServiceProvider.GetRequiredService<IFileSystem>();
        var match = loadContext.Loaded.FirstOrDefault(candidate =>
            candidate is IExtensionLifecycle && Matches(candidate, extension, fileSystem));
        await InvokeLoadedHookAsync(match, hook, hookName).ConfigureAwait(false);
    }

    private async Task DisableFailedExtensionsAsync(CancellationToken cancellationToken)
    {
        if (loadContext.Failed.Count == 0)
        {
            return;
        }

        const string reason = "The extension assembly failed to load.";
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IExtensionRepository>();

        foreach (var extension in loadContext.Failed)
        {
            try
            {
                var disabled = extension with
                {
                    Enabled = false,
                    DisabledReason = reason,
                    ModifiedAt = timeProvider.GetUtcNow().UtcDateTime
                };
                await repository.UpdateAsync(disabled, modifiedByUserId: null, cancellationToken)
                    .ConfigureAwait(false);
                await eventBus.PublishAsync(
                    ExtensionEventBuilder.BuildExtensionDisabledEvent(disabled.Id, reason),
                    cancellationToken)
                    .ConfigureAwait(false);
                logger.LogWarning(
                    "The lifecycle manager disabled extension {PackageId} ({ExtensionId}) after a load failure",
                    extension.PackageId,
                    extension.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(
                    ex,
                    "The lifecycle manager failed to disable extension {PackageId} ({ExtensionId}) " +
                    "after a load failure, so the extension is still enabled",
                    extension.PackageId,
                    extension.Id);
            }
        }
    }

    private async Task InvokeLoadedHookAsync(
        IExtension? match,
        Func<IExtensionLifecycle, ExtensionLifecycleContext, Task> hook,
        string hookName)
    {
        if (match is not IExtensionLifecycle lifecycle)
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var context = CreateContext(scope.ServiceProvider);

        try
        {
            await hook(lifecycle, context).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Extension {ExtensionName} threw an exception in {HookName}. The lifecycle manager will continue",
                match.Name,
                hookName);
        }
    }

    private async Task DispatchOnExtensionInstallAsync(
        IExtensionLifecycle lifecycle,
        IExtension loaded,
        Extension row,
        IExtensionRepository repository,
        ExtensionLifecycleContext context,
        CancellationToken cancellationToken)
    {
        if (!await TryInvokeAsync(
            () => lifecycle.OnExtensionInstallAsync(context, cancellationToken),
            loaded.Name,
            nameof(IExtensionLifecycle.OnExtensionInstallAsync)).ConfigureAwait(false))
        {
            return;
        }

        var currentVersion = loaded.Version.ToString();
        row.LifecycleVersion = currentVersion;
        try
        {
            await repository.UpdateAsync(row, modifiedByUserId: null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "The lifecycle manager failed to record version {LifecycleVersion} for extension " +
                "{ExtensionName}. The hooks will run again at startup",
                currentVersion,
                row.Name);
        }
    }

    private async Task DispatchOnExtensionUpgradeAsync(
        IExtensionLifecycle lifecycle,
        IExtension loaded,
        Extension row,
        IExtensionRepository repository,
        ExtensionLifecycleContext context,
        CancellationToken cancellationToken)
    {
        if (Version.TryParse(row.LifecycleVersion, out var previousVersion))
        {
            if (!await TryInvokeAsync(
                () => lifecycle.OnExtensionUpgradeAsync(context, previousVersion, cancellationToken),
                loaded.Name,
                nameof(IExtensionLifecycle.OnExtensionUpgradeAsync)).ConfigureAwait(false))
            {
                return;
            }
        }

        var currentVersion = loaded.Version.ToString();
        row.LifecycleVersion = currentVersion;
        try
        {
            await repository.UpdateAsync(row, modifiedByUserId: null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "The lifecycle manager failed to record version {LifecycleVersion} for extension " +
                "{ExtensionName}. The hooks will run again at startup",
                currentVersion,
                row.Name);
        }
    }

    private async Task<bool> TryInvokeAsync(Func<Task> hook, string extensionName, string hookName)
    {
        try
        {
            await hook().ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Extension {ExtensionName} threw an exception in {HookName}. The lifecycle manager skipped the hook",
                extensionName,
                hookName);
            return false;
        }
    }
}
