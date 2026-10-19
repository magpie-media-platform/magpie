# Publishing an extension

[Documentation](../README.md) / [Development](README.md)

An extension is published as a NuGet package. It has to turn up in a feed search, load in the host, and work on the host versions it declares. This page continues with the Scratch project from [Creating an extension](extensions.md) and the [Doctor walkthrough](diagnostics.md), which leaves it at version `1.0.1`.

## Assembling the runtime package

The package ID must match the entry assembly name, because [first-start discovery](architecture.md#extension-loading-and-lifetime) looks for `<PackageId>.dll`. The entry assembly contains exactly one public, concrete [`IExtension`](../reference/extensions.md#entry-point) with a public parameterless constructor, and its extension ID stays stable between releases. A minimal Scratch package looks like this:

```text
Magpie.Extensions.Scratch.nuspec
lib/
  net10.0/
    Magpie.Extensions.Scratch.dll
```

Magpie extracts the package as is and does not restore NuGet dependencies, so any private runtime dependency has to be in the package, beside the entry assembly in `lib/<framework>`. Installation extracts one `lib/<framework>` directory, the first of `net10.0`, `net9.0`, `net8.0`, `netstandard2.1`, and `netstandard2.0` that the package contains. The loader tries the assemblies the host supplies first and then the extension's own files, and a bundled copy of a host contract does not override the host's compatibility rules.

First-party projects include their runtime dependencies through [Directory.Build.targets](../../src/Directory.Build.targets). An external SDK-style project can use the same target:

```xml
<PropertyGroup>
  <TargetsForTfmSpecificBuildOutput>$(TargetsForTfmSpecificBuildOutput);PackExtensionDependencies</TargetsForTfmSpecificBuildOutput>
</PropertyGroup>

<Target Name="PackExtensionDependencies" DependsOnTargets="ResolvePackageAssets">
  <ItemGroup>
    <BuildOutputInPackage Include="@(RuntimeCopyLocalItems)" />
  </ItemGroup>
</Target>
```

Open the resulting `.nupkg` as a ZIP archive and check that every runtime file is there. Native libraries and external executables need their own deployment test, and the extension's README should say what it needs. When a distribution installs packages locally, it keeps the `.nuspec` and `lib/<framework>` layout and does not copy extension assemblies into the application directory. [Release verification](releases.md#verifying-package-discovery-registration-and-activation) tests this.

## Describing the package

The package ID, version, title, description, and authors are what an administrator sees before installing. Changed contents need a new version. The `magpie` tag makes the default [feed search](../administration/extensions.md#package-feeds) find the package, and the `Magpie.Sdk` dependency is what version selection reads. The README states which host versions and external services the extension needs and how to confirm it works. Keep credentials out of package files.

## Declaring compatibility

Magpie uses two mechanisms. When it picks a version from a feed, it reads the `Magpie.Sdk` dependency to find each candidate's minimum SDK. At installation it reads the SDK reference of the entry assembly, and at startup the loader checks that reference again and applies the entry point's optional `TargetABI`.

The host ABI must have the same major version as the SDK the extension was built against, and must not be older than it. An extension built against SDK `1.4.0` needs a host ABI of `1.4.0` or later in major version 1. Declare a bounded range, with the SDK version you build and test against as the lower bound:

```xml
<PackageReference Include="Magpie.Sdk" Version="[1.4.0,2.0.0)" />
```

First-party packages do the same. Magpie reads only the lower bound, so `TargetABI` narrows the host range further. It can only add a restriction. It cannot allow another major version.

| `TargetABI` value | Additional runtime restriction |
| --- | --- |
| Omitted or `null` | None |
| `"1.4.0"` | Exactly ABI `1.4.0` |
| `"[1.4.0,1.6.0)"` | At least `1.4.0`, and below `1.6.0` |

The oldest SDK that has the needed contracts is the one to build against. ABI checks compare versions only, so test the host versions you claim to support. The implementation is in [the package service](../../src/Magpie.Core/Extensions/ExtensionService.cs), [the loader](../../src/Magpie.Core/Extensions/ExtensionLoader.cs), and [ABI range handling](../../src/Magpie.Core/Extensions/AbiVersionExtensions.cs).

## Verifying the package before publication

Pack with `dotnet pack -c Release`, then upload the package to a clean test instance that has none of the extension's private dependencies, which is how missing ones show up. Restart, then check the version and enabled state in the extension details and the load message in the host log. Complete the verification task from the README. For Scratch `1.0.1` that is saving a path, a scan, a repair, and a second scan. Also upgrade an instance that has the previous version and check that its settings remain and any [lifecycle behavior](components/lifecycle.md) runs, and try disabling and removing the extension.

## Publishing and finding the release

nuget.org is for public distribution, and a private feed for restricted distribution. Push the Scratch sample only to a private or test feed, because any package on nuget.org with the `magpie` tag appears in every Magpie installation's default search.

### Publishing to a private or test feed

Use the terminal from the first tutorial, set `$feedUrl` to the feed's push endpoint, and enter its API key when prompted. `-MaskInput` needs PowerShell 7.1 or later.

```powershell
$package = "$sample/packages/Magpie.Extensions.Scratch.1.0.1.nupkg"
$feedUrl = "<push endpoint>"
$env:NUGET_API_KEY = Read-Host "Feed API key" -MaskInput
dotnet nuget push $package --source $feedUrl --api-key $env:NUGET_API_KEY
```

A feed's push endpoint and its NuGet service-index URL can differ. Add the feed to Magpie with the service-index URL.

### Publishing to nuget.org

Create a nuget.org account and follow the official [account and API key steps](https://learn.microsoft.com/en-us/nuget/quickstart/create-and-publish-a-package-using-the-dotnet-cli#publish-the-package). In **API Keys**, create a key that can push new packages and versions, restricted to your package ID or pattern. A public extension needs its own package ID and a matching entry assembly name.

```powershell
$package = "<path to the verified .nupkg>"
$env:NUGET_API_KEY = Read-Host "NuGet API key" -MaskInput
dotnet nuget push $package --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY
```

Indexing takes a while, so confirm that nuget.org lists the release in its search results before looking for it in Magpie. See the [publication checks](https://learn.microsoft.com/en-us/nuget/nuget-org/publish-a-package).

### Finding and installing the package in Magpie

As an administrator, open **Admin > Extensions** and select **NuGet Feeds**. nuget.org is there by default. For another feed, select **Add Feed** and enter its service-index URL and any credentials needed to read it. The feed's **Discovery filter** decides which packages Magpie finds, and the default filters on the `magpie` tag. Then select **Add**, search for **Scratch directory** or your package name, install the result, and restart.

If nothing turns up, compare the feed, its filter, and the published metadata. If Magpie finds the package but does not load it, the host log has the load error. Default version selection uses stable releases compatible with the host. To test a prerelease, upload the exact package. See [Extension administration](../administration/extensions.md) for feeds, updates, and restarts.

---

[Development](README.md) | [Documentation](../README.md)
