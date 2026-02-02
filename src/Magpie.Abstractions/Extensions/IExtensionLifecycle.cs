using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Abstractions.Extensions;

/// <summary>
/// Provides lifecycle hooks for extensions.
/// </summary>
/// <remarks>
/// Extensions that need to perform initialization after the DI container is built,
/// or handle install/uninstall events, should implement this interface in addition
/// to <see cref="IExtension"/>.
/// </remarks>
public interface IExtensionLifecycle : IExtension
{
    /// <summary>
    /// Called on every application startup after DI is built and the database is ready.
    /// </summary>
    /// <remarks>
    /// Use this hook for creating default triggers, warming caches, or other
    /// initialization that requires access to services and the database.
    /// </remarks>
    /// <param name="context">The lifecycle context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task OnAppInitializeAsync(ExtensionLifecycleContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called once when the extension is first installed.
    /// </summary>
    /// <remarks>
    /// Use this hook for one-time setup such as creating initial data or configuration.
    /// This method is guaranteed to run exactly once across all nodes.
    /// </remarks>
    /// <param name="context">The lifecycle context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task OnExtensionInstallAsync(ExtensionLifecycleContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when the extension is being removed.
    /// </summary>
    /// <remarks>
    /// Use this hook for cleanup such as deleting triggers, removing extension data,
    /// or other uninstallation tasks.
    /// </remarks>
    /// <param name="context">The lifecycle context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task OnExtensionUninstallAsync(ExtensionLifecycleContext context, CancellationToken cancellationToken = default);
}
