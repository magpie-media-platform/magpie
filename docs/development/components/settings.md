# Extension settings

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

An extension can declare its own settings, and Magpie displays and stores them on the extension's page. A settings provider only declares the fields. It does not implement storage. Components read the saved values through [`ISettingsService`](../using-built-in-services.md#reading-configuration-and-application-state), and they have to cope with a missing or invalid value, because Magpie doesn't check required fields, types, or options when it saves a group.

`IExtensionSettingsSchemaProvider` is for configuration shared by the whole extension. [Integration providers](../../reference/extensions.md#integration-implementation-contracts) have their own connection schemas, and a per-server credential belongs in that connection, not in a global extension group.

## Keeping declarations separate from values

The [Scratch tutorial](../extensions.md#declaring-the-settings-group) has a complete schema provider and entry point. Its group has three identifiers:

| Identifier | Sample | Purpose |
| --- | --- | --- |
| Extension ID | `ScratchExtension.ExtensionId` | Associates the group with the installed extension. |
| Group name | `Scratch` | Selects the extension-owned group. |
| Field key | `ScratchPath` | Selects a saved value within the group. |

Keep these stable across releases. A new display label doesn't need a new key, but if you rename a key or group without migrating the values, components can no longer read the existing configuration.

Register the schema in [`IExtension.RegisterServices`](../../reference/extensions.md#entry-point):

```csharp
services.AddSingleton<IExtensionSettingsSchemaProvider, ScratchSettings>();
```

Each provider contributes groups, and shouldn't declare the same group name twice. Return declarations synchronously and without remote requests. The sample [singleton](../using-built-in-services.md#service-lifetime-and-ownership) is stateless and must not keep scoped services. Magpie calls `GetSettingsGroups` on every provider for each settings request and doesn't catch exceptions, so one provider that throws stops the settings list for every extension.

Declare field types, options, defaults, and secret flags accurately. `DefaultValue` fills the form field, but Magpie stores it only when an administrator saves the group, so apply the same default in the component when the key is absent. `Required` marks the field in the form and doesn't prevent a save.

The settings API leaves stored secret values out of ordinary responses and reports only whether each is set. `ISettingsService` does return secrets to components, so keep them out of logs, test messages, findings, and any [UI asset](ui-assets.md).

## Testing candidate values

[`IExtensionConfigurationTester`](../../reference/extensions.md#settings-and-lifecycle) adds a **Test** operation to one group. The host uses the first tester with the matching extension ID and group name and passes it the candidate configuration, which can differ from what is saved. Test the values in that dictionary, not the saved settings.

The dictionary isn't always complete. The settings form leaves out a secret field the user didn't change, and the host doesn't fill in the saved value. Decide how your tester handles an omitted secret. The simplest policy is to ask for the credential again and say so in the message. Always test a submitted secret as given.

Don't inject `ISettingsService` into a tester's constructor. That service resolves the testers, so it creates a dependency cycle. If you fall back to a saved secret, do it only for an omitted one.

Return a failed result instead of throwing, because an exception from `TestConfigurationAsync` reaches the browser as a server error. For the Scratch project, this complete component tests whether the candidate path names an existing directory:

```csharp
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Sdk.Configuration;
using Magpie.Sdk.Services;

namespace Magpie.Extensions.Scratch;

internal sealed class ScratchConfigurationTest(IFileSystem files)
    : IExtensionConfigurationTester
{
    /// <inheritdoc />
    public Guid ExtensionId => ScratchExtension.ExtensionId;

    /// <inheritdoc />
    public string SettingsGroupName => "Scratch";

    /// <inheritdoc />
    public Task<ConfigurationTestResult> TestConfigurationAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        settings.TryGetValue("ScratchPath", out var path);
        var exists = !string.IsNullOrWhiteSpace(path)
            && Path.IsPathFullyQualified(path)
            && files.DirectoryExists(path);

        return Task.FromResult(new ConfigurationTestResult
        {
            Success = exists,
            Message = exists
                ? "The scratch directory exists."
                : "Select an existing absolute directory on the Magpie host.",
            FailureKind = exists
                ? ConfigurationFailureKind.None
                : ConfigurationFailureKind.Fatal
        });
    }
}
```

Register it beside the schema:

```csharp
services.AddScoped<IExtensionConfigurationTester, ScratchConfigurationTest>();
```

The test only reads the file system. It doesn't create the directory, save settings, or check write access. The [Doctor example](../diagnostics.md) adds a separate confirmed action to create a missing directory.

For a network configuration, set a timeout on the request to the configured service, forward cancellation, and classify the failure with `ConfigurationFailureKind`. The message should say what was tested and what failed, without credentials. A successful test is true only at the time of the test.

See the [schema contract](../../../src/Magpie.Sdk/Configuration/IExtensionSettingsSchemaProvider.cs) and [tester contract](../../../src/Magpie.Sdk/Configuration/IExtensionConfigurationTester.cs).

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
