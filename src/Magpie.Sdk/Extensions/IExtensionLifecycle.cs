using System;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Sdk.Extensions;

/// <summary>
/// An extension with lifecycle hooks.
/// </summary>
public interface IExtensionLifecycle : IExtension
{
    /// <summary>
    /// Runs when the application starts.
    /// </summary>
    /// <param name="context">The services available to the hook</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task OnAppInitializeAsync(ExtensionLifecycleContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>
    /// Runs when Magpie installs the extension.
    /// </summary>
    /// <param name="context">The services available to the hook</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task OnExtensionInstallAsync(ExtensionLifecycleContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>
    /// Runs when Magpie upgrades the extension.
    /// </summary>
    /// <param name="context">The services available to the hook</param>
    /// <param name="previousVersion">The version of the extension before the upgrade</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task OnExtensionUpgradeAsync(
        ExtensionLifecycleContext context,
        Version previousVersion,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>
    /// Runs when Magpie disables the extension.
    /// </summary>
    /// <param name="context">The services available to the hook</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task OnExtensionDisableAsync(ExtensionLifecycleContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>
    /// Runs when Magpie uninstalls the extension.
    /// </summary>
    /// <param name="context">The services available to the hook</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task OnExtensionUninstallAsync(ExtensionLifecycleContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
