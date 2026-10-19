# Adding a Doctor check and repair

[Documentation](../README.md) / [Development](README.md)

[Magpie Doctor](../administration/doctor.md) scans for problems and offers fixes, and extensions can add to both. A [check](components/doctor-checks.md) observes state and returns findings. A [repair](components/doctor-repairs.md) changes something, and only runs when an administrator asks for it. This part adds a check that reports a missing directory and a repair that creates it, continuing the **Scratch directory** project from [Creating an extension](extensions.md). The host creates the directory with its own permissions, so a disposable path on a single-node test instance suits this.

## How Doctor calls the extension

A Doctor scan is a [background job](components/jobs.md). The host builds a [`DiagnosticContext`](../reference/diagnostics.md#diagnostic-context) from the configured media servers, root folders, download clients, and indexer feeds, and calls `IDiagnosticCheck.RunAsync` on each registered check. A check that completes replaces its stored findings. The host assigns the stored finding IDs, check ID, and scan time, but the sample sets them too so a unit test sees a complete result.

The Scratch check returns one of three results. With no absolute path configured, it returns a configuration warning. If the directory exists, it returns nothing. If the path is absolute but the directory is absent, it returns a missing-directory warning that names a repair.

A check does not fix things itself. A scheduled scan never changes files, which is why the repair is a separate operation.

## Adding the check

Add `ScratchDirectoryCheck.cs`:

```csharp
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Diagnostics;
using Magpie.Sdk.Services;

namespace Magpie.Extensions.Scratch;

internal sealed class ScratchDirectoryCheck(
    ISettingsService settings,
    IFileSystem files,
    TimeProvider time) : IDiagnosticCheck
{
    internal const string CheckId = "scratch.directory";
    internal const string CreateRepairId = "scratch.directory.create";

    /// <inheritdoc />
    public string DiagnosticCheckId => CheckId;

    /// <inheritdoc />
    public string Category => "Scratch";

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiagnosticFinding>> RunAsync(
        DiagnosticContext context,
        CancellationToken cancellationToken = default)
    {
        var group = await settings.GetExtensionGroupAsync(
            ScratchExtension.ExtensionId,
            "Scratch",
            cancellationToken);
        var path = group?.Group.Settings
            .FirstOrDefault(setting => setting.Key == "ScratchPath")?.Value;

        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return
            [
                new()
                {
                    Id = Guid.NewGuid(),
                    DiagnosticCheckId = CheckId,
                    Severity = DiagnosticFindingSeverity.Warning,
                    Title = "Set the scratch directory",
                    Detail = "Save an absolute path in the Scratch extension settings.",
                    DetectedAt = time.GetUtcNow().UtcDateTime
                }
            ];
        }

        if (files.DirectoryExists(path))
        {
            return [];
        }

        return
        [
            new()
            {
                Id = Guid.NewGuid(),
                DiagnosticCheckId = CheckId,
                Severity = DiagnosticFindingSeverity.Warning,
                Title = "The scratch directory is missing",
                Detail = $"Create the configured directory: {path}",
                AffectedEntity = path,
                RepairId = CreateRepairId,
                DetectedAt = time.GetUtcNow().UtcDateTime
            }
        ];
    }
}
```

[`IFileSystem`](using-built-in-services.md#using-shared-facilities) gives access to host files and can be replaced with a fake in tests. This check does not use the integration lists in `DiagnosticContext`. The finding puts the path in `AffectedEntity` and sets `RepairId` to connect it to a registered repair.

In `ScratchExtension.cs`, add `using Magpie.Sdk.Diagnostics;` and register the check in `RegisterServices`:

```csharp
services.AddScoped<IDiagnosticCheck, ScratchDirectoryCheck>();
```

It is [scoped](using-built-in-services.md#service-lifetime-and-ownership) because it uses scoped settings and file services. Magpie adds it to its other checks.

## Adding the repair

A finding describes an earlier observation. If an administrator changes `ScratchPath` after a scan and then requests the repair on the old finding, a repair that trusted the finding would create a directory for the wrong configuration. So the repair reads the setting again, checks the check ID and repair ID, and compares the current path with `AffectedEntity`. If they differ, it fails and asks for a new scan. Creating a directory that already exists has no effect, so the repair is safe to repeat.

Add `CreateScratchDirectory.cs`:

```csharp
using Magpie.Models.Api;
using Magpie.Models.Data;
using Magpie.Sdk.Diagnostics;
using Magpie.Sdk.Services;
using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Scratch;

internal sealed class CreateScratchDirectory(
    ISettingsService settings,
    IFileSystem files,
    ILogger<CreateScratchDirectory> logger) : IDiagnosticRepair
{
    /// <inheritdoc />
    public string RepairId => ScratchDirectoryCheck.CreateRepairId;

    /// <inheritdoc />
    public async Task<DiagnosticRepairResult> RunAsync(
        DiagnosticFinding finding,
        CancellationToken cancellationToken = default)
    {
        var group = await settings.GetExtensionGroupAsync(
            ScratchExtension.ExtensionId,
            "Scratch",
            cancellationToken);
        var path = group?.Group.Settings
            .FirstOrDefault(setting => setting.Key == "ScratchPath")?.Value;

        if (finding.DiagnosticCheckId != ScratchDirectoryCheck.CheckId ||
            finding.RepairId != RepairId ||
            string.IsNullOrWhiteSpace(path) ||
            !Path.IsPathFullyQualified(path) ||
            !string.Equals(path, finding.AffectedEntity, StringComparison.Ordinal))
        {
            return DiagnosticRepairResult.Failed("The configuration changed. Run Doctor again.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            files.CreateDirectory(path);
            return DiagnosticRepairResult.Succeeded("The scratch directory exists.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Cannot create scratch directory {Path}", path);
            return DiagnosticRepairResult.Failed("Cannot create the directory. Check its path and permissions.");
        }
    }
}
```

Register it beside the check:

```csharp
services.AddScoped<IDiagnosticRepair, CreateScratchDirectory>();
```

Directory creation is synchronous, so the cancellation token can only stop the repair before it starts. A check can also have no repair. A check without a repair omits the repair class and `RepairId` and describes the manual step in `Detail`. Credentials do not belong in finding text or logs.

## Installing the new version and trying it

Change the package `Version` in the project file from `1.0.0` to `1.0.1`, pack it as before, upload `Magpie.Extensions.Scratch.1.0.1.nupkg` from **Admin > Extensions**, and restart Magpie. The host log should contain `The extension loader loaded extension Scratch directory v1.0.1`.

Save an absolute path that does not exist, under a parent the host account can write to, in the extension's **Scratch directory** setting. Open **System Status > Magpie Doctor** as an administrator and select **Run now**. When the scan finishes, **Last scanned** changes and **The scratch directory is missing** appears. Select **Fix** on it, then **Fix** again in **Apply repair?**. The directory now exists. The finding stays visible until the next scan replaces it, so select **Run now** again to confirm that it is gone.

## Verifying a changed setting

Take two disposable paths, A and B, that do not exist. Save A and run Doctor, then save B without scanning again, and request the repair on the finding for A. The repair returns **The configuration changed. Run Doctor again.** and creates neither directory. After another scan the finding names B, and its repair creates B.

If another scan has already replaced the finding you selected, the host returns **The finding no longer exists.** instead. Reloading the findings shows the current one.

## Testing it

Test the check and repair separately, with a fake settings service, a fake `IFileSystem`, and a controlled `TimeProvider`, following the [repository's NUnit conventions](../../AGENTS.md#tests). The cases that matter are a missing or relative setting, an existing directory, an absent directory, a changed setting or ID, a creation error, and cancellation before creation.

If a check throws, the scan logs the exception and skips that check, and its previous findings stay stored. **Last scanned** records when the whole scan completed, not that every check did. Check and repair IDs must be unique and stable across versions. In a [cluster](../administration/clustering.md), a check for a node-local resource has to say which node can observe and repair it.

See the [check guide](components/doctor-checks.md), the [repair guide](components/doctor-repairs.md), and the [diagnostic reference](../reference/diagnostics.md) for the contracts. When the package is ready, continue with [Publishing an extension](publish-extensions.md).

---

[Development](README.md) | [Documentation](../README.md)
