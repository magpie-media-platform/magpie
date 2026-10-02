using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;

namespace Magpie.Abstractions;

/// <summary>
/// The service contract for the lifecycle hooks of the extensions in this process.
/// </summary>
public interface IExtensionLifecycleManager
{
    /// <summary>
    /// Runs the install, upgrade, and application-initialize hooks of the loaded extensions.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task DispatchStartupHooksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the disable hook of an extension that is loaded in this process.
    /// </summary>
    /// <param name="extension">The extension to disable</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task InvokeDisableAsync(Extension extension, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the uninstall hook of an extension that is loaded in this process.
    /// </summary>
    /// <param name="extension">The extension to uninstall</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task InvokeUninstallAsync(Extension extension, CancellationToken cancellationToken = default);
}
