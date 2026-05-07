using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Models;

using Microsoft.Extensions.Logging;

namespace Magpie.App.Extensions;

/// <summary>
/// Dispatches lifecycle hooks for loaded extensions.
/// </summary>
/// <remarks>
/// Tracks which extensions have already been installed in a small JSON file under
/// the extensions directory so that <see cref="IExtensionLifecycle.OnExtensionInstallAsync"/>
/// runs once per extension, while <see cref="IExtensionLifecycle.OnAppInitializeAsync"/>
/// runs every host startup.
/// </remarks>
public sealed class ExtensionLifecycleDispatcher
{
    /// <summary>
    /// The file name of the install-tracking record relative to the extensions directory.
    /// </summary>
    public const string InstalledRecordFileName = ".installed.json";

    private readonly ILogger<ExtensionLifecycleDispatcher> _logger;

    /// <summary>Initializes a new instance.</summary>
    public ExtensionLifecycleDispatcher(ILogger<ExtensionLifecycleDispatcher> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Invokes lifecycle hooks for all loaded extensions that implement
    /// <see cref="IExtensionLifecycle"/>.
    /// </summary>
    /// <param name="extensions">The loaded extensions whose hooks should fire.</param>
    /// <param name="extensionsRoot">The root directory used to persist install-tracking state.</param>
    /// <param name="context">The lifecycle context passed to each hook.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task DispatchAsync(
        IReadOnlyList<LoadedExtension> extensions,
        string extensionsRoot,
        ExtensionLifecycleContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentNullException.ThrowIfNull(context);

        if (extensions.Count == 0)
        {
            return;
        }

        var installRecord = await LoadInstalledRecordAsync(extensionsRoot, cancellationToken).ConfigureAwait(false);
        var installRecordChanged = false;

        foreach (var loaded in extensions)
        {
            if (loaded.Instance is not IExtensionLifecycle lifecycle)
            {
                continue;
            }

            var key = loaded.Instance.Id.ToString();
            if (!installRecord.Contains(key))
            {
                if (await TryInvokeAsync(
                    () => lifecycle.OnExtensionInstallAsync(context, cancellationToken),
                    loaded.Instance.Name,
                    nameof(IExtensionLifecycle.OnExtensionInstallAsync)).ConfigureAwait(false))
                {
                    installRecord.Add(key);
                    installRecordChanged = true;
                }
            }

            await TryInvokeAsync(
                () => lifecycle.OnAppInitializeAsync(context, cancellationToken),
                loaded.Instance.Name,
                nameof(IExtensionLifecycle.OnAppInitializeAsync)).ConfigureAwait(false);
        }

        if (installRecordChanged)
        {
            await SaveInstalledRecordAsync(extensionsRoot, installRecord, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<HashSet<string>> LoadInstalledRecordAsync(
        string extensionsRoot,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(extensionsRoot) || !Directory.Exists(extensionsRoot))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var path = Path.Combine(extensionsRoot, InstalledRecordFileName);
        if (!File.Exists(path))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var entries = await JsonSerializer
                .DeserializeAsync<string[]>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return new HashSet<string>(entries ?? [], StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to read installed-extensions record at '{Path}'. Treating all extensions as freshly installed.",
                path);
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task SaveInstalledRecordAsync(
        string extensionsRoot,
        HashSet<string> record,
        CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(extensionsRoot);
            var path = Path.Combine(extensionsRoot, InstalledRecordFileName);
            await using var stream = File.Create(path);
            await JsonSerializer
                .SerializeAsync(stream, record.ToArray(), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to persist installed-extensions record under '{ExtensionsRoot}'.",
                extensionsRoot);
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
            _logger.LogError(
                ex,
                "Extension '{ExtensionName}' threw during {HookName}. It will be skipped for this hook.",
                extensionName,
                hookName);
            return false;
        }
    }
}
