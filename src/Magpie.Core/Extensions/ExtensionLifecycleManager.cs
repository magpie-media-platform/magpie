using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Models;
using Magpie.Models.Arguments;
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
    IExtensionRepository repository,
    IFileSystem fileSystem,
    MagpieHostEnvironment hostEnvironment,
    ILoggerFactory loggerFactory,
    ILogger<ExtensionLifecycleManager> logger) : IExtensionLifecycleManager
{
    private static string ResolveAssemblyPath(Assembly assembly)
    {
        return AssemblyLoadContext.GetLoadContext(assembly) is ExtensionLoader.ExtensionAssemblyLoadContext context
            ? context.MainAssemblyPath
            : assembly.Location;
    }

    private static bool Matches(IExtension loaded, Extension extension, IFileSystem fileSystem)
    {
        var assembly = loaded.GetType().Assembly;
        if (!string.IsNullOrWhiteSpace(extension.AssemblyName) &&
            string.Equals(assembly.GetName().Name, extension.AssemblyName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var assemblyPath = ResolveAssemblyPath(assembly);
        if (string.IsNullOrWhiteSpace(extension.FilePath) || string.IsNullOrWhiteSpace(assemblyPath))
        {
            return false;
        }

        return fileSystem.PathsEqual(assemblyPath, extension.FilePath);
    }

    public async Task DispatchStartupHooksAsync(CancellationToken cancellationToken = default)
    {
        await DisableFailedExtensionsAsync(cancellationToken).ConfigureAwait(false);

        if (!loadContext.Loaded.Any(extension => extension is IExtensionLifecycle))
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var context = CreateContext(scope.ServiceProvider);
        var rows = (await repository.ListAllAsync(cancellationToken).ConfigureAwait(false)).ToArray();

        foreach (var extension in loadContext.Loaded)
        {
            if (extension is not IExtensionLifecycle lifecycle)
            {
                continue;
            }

            var assembly = extension.GetType().Assembly;
            var runtimeName = assembly.GetName().Name;
            var runtimePath = ResolveAssemblyPath(assembly);
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
                    "The lifecycle manager skipped the install and upgrade hooks for extension " +
                    "{ExtensionName} because it has no desired extension row",
                    extension.Name);
            }
            else
            {
                var currentVersion = extension.Version.ToString();
                if (string.IsNullOrWhiteSpace(row.LifecycleVersion))
                {
                    await DispatchVersionHookAsync(
                        () => lifecycle.OnExtensionInstallAsync(context, cancellationToken),
                        nameof(IExtensionLifecycle.OnExtensionInstallAsync),
                        extension,
                        row,
                        cancellationToken).ConfigureAwait(false);
                }
                else if (!string.Equals(row.LifecycleVersion, currentVersion, StringComparison.Ordinal))
                {
                    await DispatchVersionHookAsync(
                        Version.TryParse(row.LifecycleVersion, out var previousVersion)
                            ? () => lifecycle.OnExtensionUpgradeAsync(context, previousVersion, cancellationToken)
                            : () => Task.CompletedTask,
                        nameof(IExtensionLifecycle.OnExtensionUpgradeAsync),
                        extension,
                        row,
                        cancellationToken).ConfigureAwait(false);
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

    private ExtensionLifecycleContext CreateContext(IServiceProvider scopedServices)
    {
        return new(hostEnvironment.NodeId, scopedServices, loggerFactory);
    }

    private async Task InvokeHookAsync(
        Extension extension,
        Func<IExtensionLifecycle, ExtensionLifecycleContext, Task> hook,
        string hookName)
    {
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

        foreach (var extension in loadContext.Failed)
        {
            try
            {
                var disabled = await repository.PatchAsync(
                    new ExtensionPatch
                    {
                        Id = extension.Id,
                        Enabled = new() { Value = false },
                        DisabledReason = new() { Value = reason }
                    },
                    modifiedByUserId: null,
                    cancellationToken).ConfigureAwait(false);
                if (!disabled)
                {
                    continue;
                }

                await eventBus.PublishAsync(
                    ExtensionEventBuilder.BuildExtensionDisabledEvent(extension.Id, reason),
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
                    "after a load failure",
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
            logger.LogError(ex, "Extension {ExtensionName} threw an exception in {HookName}", match.Name, hookName);
        }
    }

    private async Task DispatchVersionHookAsync(
        Func<Task> hook,
        string hookName,
        IExtension loaded,
        Extension row,
        CancellationToken cancellationToken)
    {
        if (!await TryInvokeAsync(hook, loaded.Name, hookName).ConfigureAwait(false))
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
                "The lifecycle manager failed to record version {LifecycleVersion} for extension {ExtensionName}",
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
            logger.LogError(ex, "Extension {ExtensionName} threw an exception in {HookName}", extensionName, hookName);
            return false;
        }
    }
}
