# Creating an extension

[Documentation](../README.md) / [Development](README.md)

An [extension](../administration/extensions.md) is a class library that Magpie loads from a NuGet package. This tutorial builds **Scratch directory**, an extension with one [saved setting](components/settings.md), and loads it into a Magpie you already run.

## Creating the project

Building an extension requires the .NET 10 SDK and a test Magpie with [administrator access](../reference/roles.md).

```powershell
dotnet new classlib -n Magpie.Extensions.Scratch -f net10.0
cd Magpie.Extensions.Scratch
dotnet add package Magpie.Sdk
```

`Magpie.Sdk` comes from nuget.org. Magpie loads an extension built against an SDK of the same major version as the host, and not newer than it. `./magpie version` shows the host version, and `--version` on `dotnet add package` selects a matching SDK.

Add these properties to the existing `PropertyGroup` of `Magpie.Extensions.Scratch.csproj`:

```xml
<PackageId>Magpie.Extensions.Scratch</PackageId>
<Version>1.0.0</Version>
<Title>Scratch directory</Title>
<Description>Configure a scratch directory for the Doctor example.</Description>
<Authors>Example author</Authors>
<PackageTags>magpie</PackageTags>
```

The package ID matches the assembly name. The `magpie` tag is what the default feed search looks for.

## Declaring the settings group

Delete `Class1.cs` and create `ScratchExtension.cs`. The entry point is the one public class that implements `IExtension`, and `RegisterServices` registers what the extension provides:

```csharp
using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Sdk.Configuration;
using Magpie.Sdk.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Magpie.Extensions.Scratch;

/// <summary>
/// Adds the scratch directory settings.
/// </summary>
public sealed class ScratchExtension : IExtension
{
    internal static readonly Guid ExtensionId =
        new("5647812b-e9f8-4942-aaf4-bf0b3f52ec20");

    /// <inheritdoc />
    public Guid Id => ExtensionId;

    /// <inheritdoc />
    public string Name => "Scratch directory";

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<IExtensionSettingsSchemaProvider, ScratchSettings>();
    }
}

internal sealed class ScratchSettings : IExtensionSettingsSchemaProvider
{
    /// <inheritdoc />
    public IEnumerable<ExtensionSettingsGroupDefinition> GetSettingsGroups()
    {
        yield return new()
        {
            ExtensionId = ScratchExtension.ExtensionId,
            Name = "Scratch",
            SettingsSchema =
            [
                new()
                {
                    Key = "ScratchPath",
                    Type = DataType.String,
                    DisplayName = "Scratch directory",
                    Description = "An absolute directory path on the Magpie host.",
                    Required = true
                }
            ]
        };
    }
}
```

The entry point has no constructor dependencies, because the loader creates it before the host's service provider exists. `RegisterServices` only registers the settings provider. Magpie asks the provider for its group definitions and shows the group with the extension. Code reads the saved value through the extension ID, the group name `Scratch`, and the field key `ScratchPath`, which is what the Doctor check in the next part does.

The extension ID must stay stable between versions. The group name and field key identify stored data, so changing either means the extension no longer finds its saved values. The loader requires exactly one public, concrete `IExtension` in the entry assembly. The provider can be a [singleton](using-built-in-services.md#service-lifetime-and-ownership) because its declaration is fixed.

## Building and loading

```powershell
dotnet pack -c Release -o packages
```

As an administrator, open **Admin > Extensions**, select **Add**, then **Upload .nupkg**, and choose `Magpie.Extensions.Scratch.1.0.0.nupkg` from the `packages` directory. Restart Magpie.

Select **Scratch directory** in **Admin > Extensions**. In the **Settings** section, enter a path and select **Save**. The value is still there after a reload.

If the upload or load fails, the error and the host log name the problem. For other hosts, see [Loading and verifying the extension](../administration/install-extensions.md#loading-and-verifying-the-extension).

## Next steps

- [Add a Doctor check and repair](diagnostics.md) for this setting.
- [Call built-in services](using-built-in-services.md) from your code.
- [Add other components](components/README.md), such as indexers or metadata providers.
- [Publish the package](publish-extensions.md).
- To run Magpie from source, see [local setup](local-setup.md). [Extension contracts](../reference/extensions.md) lists the exact types.

---

[Development](README.md) | [Documentation](../README.md)
