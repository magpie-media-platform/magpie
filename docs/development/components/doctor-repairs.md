# Doctor repairs

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

A Doctor repair fixes the problem a [stored finding](../../reference/diagnostics.md#finding-fields) describes, when an administrator asks for it in the [Doctor interface](../../administration/doctor.md#applying-and-verifying-a-repair). Repairs are optional. A [check](doctor-checks.md) can just report a problem that needs manual action. You write one by implementing `IDiagnosticRepair`, when the fix has a clear target and the repair can re-verify its conditions before changing anything.

A finding's `RepairId` makes the action available, and a scan never runs a repair. When asked, the host reloads the stored finding by ID and runs the repair registered under its `RepairId`. If two repairs share an ID, the [last registered](README.md#registration-order) wins, so use unique, stable IDs. The host returns an unsuccessful result if the finding is gone, has no repair ID, or has no registered repair. Finding IDs change whenever a later scan replaces the results, so the finding on a user's screen can be stale by the time the repair runs.

Register the repair [scoped](../using-built-in-services.md#service-lifetime-and-ownership) in [`IExtension.RegisterServices`](../../reference/extensions.md#entry-point), with a `RepairId` that matches the finding:

```csharp
services.AddScoped<IDiagnosticRepair, CreateScratchDirectory>();
```

## Rechecking before changing anything

A finding records an earlier observation. Between scan and repair, someone may have fixed the problem, removed the target, or changed the configuration, so read the current state and compare it with the finding. The Scratch repair from the [walkthrough](../diagnostics.md#adding-the-repair) does this after reading the current setting:

```csharp
if (finding.DiagnosticCheckId != ScratchDirectoryCheck.CheckId ||
    finding.RepairId != RepairId ||
    string.IsNullOrWhiteSpace(path) ||
    !Path.IsPathFullyQualified(path) ||
    !string.Equals(path, finding.AffectedEntity, StringComparison.Ordinal))
{
    return DiagnosticRepairResult.Failed("The configuration changed. Run Doctor again.");
}
```

Here `finding` is the method input, `RepairId` is the repair's property, and `path` is the saved setting. If the path changed after the scan, a stale finding cannot create either directory. Make a repeated run harmless too, so a repair whose work is already done succeeds again. Make changes through the supported [application services](../using-built-in-services.md) rather than writing their state directly, and forward cancellation.

## Results

Return `DiagnosticRepairResult.Succeeded(message)` only for state the repair actually set, and `Failed(message)` for a known failed precondition or operation, with the next useful step. An exception from the repair reaches the caller as a failed request. A successful repair does not remove the finding or run the check, so Doctor has to scan again to confirm the fix. See [DiagnosticRepairResult](../../reference/diagnostics.md#repair-result).

See [IDiagnosticRepair](../../../src/Magpie.Sdk/Diagnostics/IDiagnosticRepair.cs) and the [walkthrough](../diagnostics.md#verifying-a-changed-setting) for a stale-finding test.

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
