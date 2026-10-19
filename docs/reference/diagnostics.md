# Diagnostic contracts

[Documentation](../README.md) / [Reference](README.md)

[Doctor](../administration/doctor.md) is built on two contracts, both [additive extension components](../development/components/README.md#additional-components). [`IDiagnosticCheck`](#check-contract) observes a condition, and [`IDiagnosticRepair`](#repair-contract) changes state for a stored finding. [`IDiagnosticsService`](#diagnostics-service) reads findings and requests repairs. For a worked example, see [Diagnostics](../development/diagnostics.md).

**On this page**

- [Check contract](#check-contract)
- [Diagnostic context](#diagnostic-context)
- [Finding fields](#finding-fields)
- [Finding events](#finding-events)
- [Repair contract](#repair-contract)
- [Repair result](#repair-result)
- [Diagnostics service](#diagnostics-service)

## Check contract

[IDiagnosticCheck](../../src/Magpie.Sdk/Diagnostics/IDiagnosticCheck.cs) is in `Magpie.Sdk.Diagnostics`.

| Member | Type or signature | Meaning |
| --- | --- | --- |
| `DiagnosticCheckId` | `string` | Stable check identity. The host uses it to replace the stored findings of the check. |
| `Category` | `string` | Category that the check declares. The host does not read this value. The `diagnostic.category` event key contains the check ID, not this value. See [finding events](#finding-events). |
| `RunAsync` | `Task<IReadOnlyList<DiagnosticFinding>> RunAsync(DiagnosticContext context, CancellationToken cancellationToken = default)` | Examine the condition and return findings. An empty list means that the check completed and found no condition. |

Register each check as `IDiagnosticCheck` in `IExtension.RegisterServices`, with a unique ID. A scan runs all registered checks.

The host replaces the stored results of each check that completes, assigning new finding IDs and setting the check ID and scan time on each finding. If a check throws, the host logs the exception, skips that check, and keeps its earlier results. Cancelling the scan token stops the scan. See [Doctor checks](../development/components/doctor-checks.md).

## Diagnostic context

[DiagnosticContext](../../src/Magpie.Sdk/Diagnostics/DiagnosticContext.cs) contains the configured host entities:

| Property | Type | Default |
| --- | --- | --- |
| `MediaServers` | `IReadOnlyList<MediaServer>` | Empty |
| `RootFolders` | `IReadOnlyList<RootFolder>` | Empty |
| `DownloadClients` | `IReadOnlyList<DownloadClient>` | Empty |
| `IndexerFeeds` | `IReadOnlyList<IndexerFeed>` | Empty |

These entity types are in `Magpie.Models.Data`. The host fills the lists before it calls the checks. Extension settings are not in the context. A check reads them through [`ISettingsService`](../development/components/settings.md). `DiagnosticContext.Empty` returns a new context with empty lists.

## Finding fields

[DiagnosticFinding](../../src/Magpie.Models/Data/DiagnosticFinding.cs) is a mutable record in `Magpie.Models.Data`.

| Property | Type | Meaning |
| --- | --- | --- |
| `Id` | `Guid` | Identity of this stored finding. The scan assigns a new value each time it replaces the findings. |
| `DiagnosticCheckId` | Required `string` | Check that produced the finding. The scan sets the ID of the registered check. |
| `Severity` | `DiagnosticFindingSeverity` | Effect of the condition. The default is `Info`. |
| `Title` | Required `string` | Short description of the condition. |
| `Detail` | Required `string` | Explanation and corrective guidance. |
| `AffectedEntity` | Nullable `string` | Record, path, or other resource that the condition affects. The check and the repair interpret this value. |
| `RepairId` | Nullable `string` | Identifier of the registered repair, if the finding has one. |
| `DetectedAt` | `DateTime` | UTC time at which the scan started. |

A direct call to a check does not store its findings. A hosted scan sets the ID and time.

The [severity enum](../../src/Magpie.Models/Enums/DiagnosticFindingSeverity.cs) has these values:

| Value | Numeric value | Meaning |
| --- | --- | --- |
| `Info` | 0 | An observation that needs no action. |
| `Warning` | 1 | A condition that needs review but does not break the system. |
| `Error` | 2 | A condition that breaks the system, or is about to break it, and needs attention. |

The interface and the logs can show the finding text and affected entity, so keep credentials and tokens out of them.

## Finding events

After a check completes, the scan compares each new finding with the stored findings. The comparison key is the check ID, the affected entity, and the title.

| Event | When the scan publishes it |
| --- | --- |
| `diagnostic.finding.raised` | No stored finding has the same key. |
| `diagnostic.finding.updated` | A stored finding has the same key, and the severity, detail, or repair ID changed. |
| `diagnostic.scan.completed` | The scan finished. The `diagnostic.findingCount` key gives the number of findings from the checks that completed. |

The raised and updated events have these context keys: `diagnostic.findingId`, `diagnostic.category`, and `diagnostic.severity`. In this release, `diagnostic.category` contains the check ID. A filter on this key must use check IDs. The scan publishes no event when a finding disappears.

A change to the title gives a new key, so the scan publishes a raised event even for the same resource.

## Repair contract

[IDiagnosticRepair](../../src/Magpie.Sdk/Diagnostics/IDiagnosticRepair.cs) is in `Magpie.Sdk.Diagnostics`.

| Member | Type or signature | Meaning |
| --- | --- | --- |
| `RepairId` | `string` | Stable identity of the action. A finding refers to it. |
| `RunAsync` | `Task<DiagnosticRepairResult> RunAsync(DiagnosticFinding finding, CancellationToken cancellationToken = default)` | Examine the finding against the current state, make the correction if it is still necessary, and report the result. |

Register each repair as `IDiagnosticRepair`, with a unique ID. The host selects the repair from the `RepairId` of the stored finding, and uses the last registered repair if several have that ID.

A repair runs against the current state, so it must handle a changed setting, an already corrected condition, a removed target, and a repeated run. See [Doctor repairs](../development/components/doctor-repairs.md).

## Repair result

[DiagnosticRepairResult](../../src/Magpie.Models/Api/DiagnosticRepairResult.cs) is in `Magpie.Models.Api`.

| Member | Meaning |
| --- | --- |
| Required `bool Success` | `true` when the repair completed its correction. |
| Required `string Message` | What the repair did, or why it did not run or did not complete. |
| `Succeeded(string message)` | Creates a result with `Success = true`. |
| `Failed(string message)` | Creates a result with `Success = false`. |

A successful repair does not delete its finding and does not start a scan. The next completed run of the check replaces the stored results. If the repair throws an unexpected exception, the exception goes to the caller.

## Diagnostics service

[IDiagnosticsService](../../src/Magpie.Sdk/Services/IDiagnosticsService.cs) is for consumption only. Do not replace it.

| Signature | Result and side effect |
| --- | --- |
| `Task<DiagnosticFindings> GetFindingsAsync(CancellationToken cancellationToken = default)` | Reads the stored findings and the time of the last scan. Does not run checks. |
| `Task<DiagnosticRepairResult> RepairAsync(Guid findingId, CancellationToken cancellationToken = default)` | Loads the finding, selects its repair, and runs the repair. Can change state. |

`RepairAsync` returns a failed result when the finding does not exist, has no repair, or names a repair that is not registered. A scan can replace finding IDs, so read the findings again before retrying.

[DiagnosticFindings](../../src/Magpie.Models/DiagnosticFindings.cs) contains the required `IReadOnlyList<DiagnosticFinding> Findings` and the nullable `DateTime LastScannedAt`. Null means no scan has completed, or the stored time cannot be read. Otherwise it is when the last scan finished, including a scan in which a check threw, so the list can hold older findings from that check.

`IDiagnosticsService` has no scan method. To request a scan, queue the `DiagnosticCheck` [job](../administration/jobs-and-automation.md#jobs-and-executions), for example through the [built-in job services](../development/using-built-in-services.md#submitting-a-job-and-report-progress).

---

[Reference](README.md) | [Documentation](../README.md)
