using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;

namespace Magpie.Abstractions;

/// <summary>
/// Provides lifecycle hooks for extensions.
/// </summary>
/// <remarks>
/// <para>
/// Implementing this interface is optional. The host loader invokes the hooks below at
/// well-defined points in the extension lifecycle. Hooks should be idempotent where
/// possible and must not throw for expected failure modes -- prefer logging and the
/// <c>Result</c> pattern over exceptions.
/// </para>
/// <para>
/// The host treats updates as an uninstall of the previous version followed by an
/// install of the new version. Extensions that need to migrate state across versions
/// should perform their migration inside <see cref="OnExtensionInstallAsync"/> by
/// detecting persisted data left behind by the prior version. There is no separate
/// update hook.
/// </para>
/// </remarks>
public interface IExtensionLifecycle : IExtension
{
    /// <summary>
    /// Called on every application startup after DI is built and the database is ready.
    /// </summary>
    /// <remarks>
    /// The host runs this hook in the background after startup; do not assume it completes
    /// before the host begins serving requests. Long-running work should still be offloaded
    /// so other extensions' hooks are not blocked.
    /// </remarks>
    /// <param name="context">The lifecycle context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task OnAppInitializeAsync(ExtensionLifecycleContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called once when the extension is first installed.
    /// </summary>
    /// <remarks>
    /// Triggered by the install endpoint after the extension assembly has been staged
    /// and DI services have been registered. Use this hook to seed configuration,
    /// create extension-owned tables or directories, and perform any one-time setup.
    /// On an update (uninstall-then-install), this hook runs again with any prior
    /// persisted state still in place; the implementation is responsible for migrating
    /// that state forward.
    /// </remarks>
    /// <param name="context">The lifecycle context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task OnExtensionInstallAsync(ExtensionLifecycleContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reserved for future use. The current host does not invoke this hook -- assemblies
    /// stay loaded under <c>AssemblyLoadContext.Default</c> until the host restarts, and
    /// the install ledger plus directory delete in the installer is the practical
    /// uninstall. Implementations should not assume this hook fires.
    /// </summary>
    /// <param name="context">The lifecycle context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task OnExtensionUninstallAsync(ExtensionLifecycleContext context, CancellationToken cancellationToken = default);
}
