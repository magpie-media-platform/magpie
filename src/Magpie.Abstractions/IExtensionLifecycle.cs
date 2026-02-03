using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;

namespace Magpie.Abstractions;

/// <summary>
/// Provides lifecycle hooks for extensions.
/// </summary>
public interface IExtensionLifecycle : IExtension
{
    /// <summary>
    /// Called on every application startup after DI is built and the database is ready.
    /// </summary>
    /// <param name="context">The lifecycle context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task OnAppInitializeAsync(ExtensionLifecycleContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called once when the extension is first installed.
    /// </summary>
    /// <param name="context">The lifecycle context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task OnExtensionInstallAsync(ExtensionLifecycleContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when the extension is being removed.
    /// </summary>
    /// <param name="context">The lifecycle context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task OnExtensionUninstallAsync(ExtensionLifecycleContext context, CancellationToken cancellationToken = default);
}
