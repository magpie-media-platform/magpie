# Extension lifecycle

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

Lifecycle hooks let an extension set up or clean up its own state when the host starts or the extension changes. You use them when an extension keeps data of its own, such as a database table, a file, or a default setting. Implement `IExtensionLifecycle` on the public entry point. It extends [`IExtension`](../../reference/extensions.md#entry-point), so the same object supplies `Id`, `Name`, and `RegisterServices`, and the hooks need no separate registration. Declare components in `RegisterServices`, and use the hooks only for work on state after the host has built its services.

## When hooks run

At startup the host finds the installed record for each loaded lifecycle entry point. With no matching record it logs a warning and skips the install and upgrade hooks, though it still calls `OnAppInitializeAsync`. Otherwise it compares the recorded version with the loaded one, which is `IExtension.Version` and defaults to the assembly's major.minor.build.

1. With no recorded version, it calls `OnExtensionInstallAsync`.
2. With a different version and a readable previous one, it calls `OnExtensionUpgradeAsync` with that previous version.
3. It calls `OnAppInitializeAsync`.

If the recorded version is unreadable, the upgrade hook is skipped and the current version is recorded. The version is recorded after a successful install or upgrade hook, so a failed hook or a failed write can run again at a later start. Make these hooks safe to repeat, and have the upgrade hook check the supplied version, since a version change can be a downgrade.

| Hook | Appropriate work |
| --- | --- |
| `OnAppInitializeAsync` | Bounded initialization needed at every start. |
| `OnExtensionInstallAsync` | Create initial extension-owned state when absent. |
| `OnExtensionUpgradeAsync` | Update extension-owned state from the supplied previous version. |
| `OnExtensionDisableAsync` | Respond to disabling a currently loaded extension. |
| `OnExtensionUninstallAsync` | Respond to removing a currently loaded extension. |

In a [cluster](../../administration/clustering.md), one node runs the install or upgrade hook for each version change, after writing a claim to the extension record. Other nodes skip it while that node is active and call `OnAppInitializeAsync` without waiting. If the claiming node is silent for more than 2 minutes, another node can take the claim at its next start and run the hook again. So `OnAppInitializeAsync` must work both before and after the install or upgrade work finishes.

The host calls the disable and uninstall hooks before it changes the extension record, and only on a node where the entry point is loaded, so an extension that failed to load gets no cleanup call. Other nodes with the extension loaded call the hook when the event arrives, and a node that misses the event does not. If a hook throws, the host logs it and completes the disable or removal. An uninstall hook must not delete media or configuration the extension does not own.

## The hook's scope

[`ExtensionLifecycleContext`](../../reference/extensions.md#settings-and-lifecycle) supplies the node ID, a [scoped](../using-built-in-services.md#service-lifetime-and-ownership) service provider, and a logger factory. This hook goes on the Scratch entry point from [Creating an extension](../extensions.md), which now implements `IExtensionLifecycle`. It needs `Magpie.Sdk.Extensions` and `Microsoft.Extensions.Logging`:

```csharp
public Task OnAppInitializeAsync(
    ExtensionLifecycleContext context,
    CancellationToken cancellationToken = default)
{
    cancellationToken.ThrowIfCancellationRequested();
    var logger = context.LoggerFactory.CreateLogger("Scratch.Extension");
    logger.LogInformation("Scratch initialized on {NodeId}", context.NodeId);
    return Task.CompletedTask;
}
```

Resolve scoped services inside the hook and finish their work before returning. Do not keep the provider, resolved services, or background tasks afterward. Use a [job](jobs.md) for work that needs scheduling, progress, cancellation, or retry. The startup dispatcher logs hook exceptions and continues, so check the extension's log to confirm a hook succeeded.

See [IExtensionLifecycle](../../../src/Magpie.Sdk/Extensions/IExtensionLifecycle.cs) and the [Cardigann entry point](../../../src/Magpie.Extensions.Cardigann/CardigannExtension.cs), a complete extension with lifecycle work.

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
