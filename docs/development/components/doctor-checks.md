# Doctor checks

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

Doctor is Magpie's health scan. Each check looks at one condition and returns [findings](../../reference/diagnostics.md#finding-fields) that tell an administrator what needs attention. You write one by implementing `IDiagnosticCheck` when your extension can detect a configuration or operating problem that people should hear about.

Checks are [additive](README.md#additional-components), and a scan runs every registered one. Give each a unique, stable `DiagnosticCheckId`, because the host replaces stored results by that ID. `Category` is a descriptive label that the host does not read in this release.

## How a scan runs

A scan is a [diagnostic job](jobs.md). The host builds a [`DiagnosticContext`](../../reference/diagnostics.md#diagnostic-context) with the configured [media servers](media-servers.md), [root folders](../../administration/storage-and-services.md#adding-a-root-folder), [download clients](download-clients.md), and [indexer feeds](indexer-feeds.md), then calls the checks one at a time with the job's cancellation token. A check can also use [scoped](../using-built-in-services.md#service-lifetime-and-ownership) services for anything the context lacks.

For each check that succeeds, the host assigns new finding IDs, sets the check ID and scan time, and replaces that check's earlier findings. An empty list clears them, so return one only after you have evaluated the condition and found nothing wrong. If a check throws, the job logs it, keeps that check's earlier findings, and carries on with the next check.

The scan raises events for new or changed findings and for completion, which [notification rules](../../administration/notifications.md) can use. A finding is identified across scans by its check ID, affected entity, and title. A new title raises a new-finding event, and a change to severity, detail, or repair ID raises an update event.

A check observes and does not change anything. A finding can point to a separately registered [repair](doctor-repairs.md) or describe a manual step.

## Writing a finding

The title names the condition, the detail explains the consequence or the fix, and `AffectedEntity` names the observed resource so a repair can compare it with the current state. Keep secrets out of all three. Choose the severity from the consequence, and set `RepairId` only when a registered repair fixes this specific condition. The [reference](../../reference/diagnostics.md) defines the fields and severities.

This finding comes from the scratch directory check in the [walkthrough](../diagnostics.md#adding-the-check), which returns it when a configured directory is missing. It uses `Magpie.Models.Data` and `Magpie.Models.Enums`:

```csharp
new DiagnosticFinding
{
    DiagnosticCheckId = ScratchDirectoryCheck.CheckId,
    Severity = DiagnosticFindingSeverity.Warning,
    Title = "The scratch directory is missing",
    Detail = $"Create the configured directory: {path}",
    AffectedEntity = path,
    RepairId = ScratchDirectoryCheck.CreateRepairId
}
```

Register the check in [`IExtension.RegisterServices`](../../reference/extensions.md#entry-point). Make it scoped when it uses scoped services, rather than a singleton that captures them:

```csharp
services.AddScoped<IDiagnosticCheck, ScratchDirectoryCheck>();
```

See [IDiagnosticCheck](../../../src/Magpie.Sdk/Diagnostics/IDiagnosticCheck.cs) and the [full walkthrough](../diagnostics.md).

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
