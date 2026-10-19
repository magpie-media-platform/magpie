# Jobs

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

A job is a unit of work that Magpie's job runtime runs for you, and an execution is one tracked attempt at it, with its own state and progress. You write one by implementing `IJob` when work needs history, progress, cancellation, or [triggers](../../administration/jobs-and-automation.md#jobs-and-executions). A [`JobSpec`](../../../src/Magpie.Models/JobSpec.cs) tells the host how the work can be started and coordinated. Registering a job only adds a job type. It starts nothing and creates no trigger. To request work from another component, use the [built-in job services](../using-built-in-services.md#submitting-a-job-and-report-progress).

## A manual job

This job reports how many [Doctor findings](doctor-checks.md) are stored and when the last scan finished. It does not run Doctor. It extends the Scratch extension from [Creating an extension](../extensions.md).

```csharp
using Magpie.Models;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;
using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Scratch;

internal sealed class DoctorSummaryJob(
    IDiagnosticsService diagnostics,
    IProgressReporter progress,
    ILogger<DoctorSummaryJob> logger) : IJob
{
    /// <inheritdoc />
    public async Task ExecuteAsync(
        JobContext context,
        CancellationToken cancellationToken = default)
    {
        progress.Report(null, "Read stored Doctor findings.");
        var result = await diagnostics.GetFindingsAsync(cancellationToken);

        logger.LogInformation(
            "Doctor has {FindingCount} stored findings; last scan: {LastScannedAt}",
            result.Findings.Count,
            result.LastScannedAt);

        var message = result.LastScannedAt is null
            ? "Doctor has not completed a scan."
            : $"Doctor has {result.Findings.Count} stored findings.";
        progress.Report(1, message);
        await progress.FlushAsync(cancellationToken);
    }
}
```

`RegisterJob<T>` registers the handler together with its specification, so do not register the handler separately. Add this to `ScratchExtension.RegisterServices`, with `Magpie.Models`, `Magpie.Models.Enums`, `Magpie.Models.Jobs`, and `Microsoft.Extensions.DependencyInjection` in scope:

```csharp
services.RegisterJob<DoctorSummaryJob>(new JobSpec
{
    JobType = "Scratch.DoctorSummary",
    DisplayName = "Report Doctor summary",
    Description = "Report the stored finding count without running a scan.",
    Category = "Scratch",
    Scope = JobScope.Administrative,
    Enabled = true,
    OnDemand = true,
    FailurePolicy = JobFailurePolicy.Fail,
    SupportedTriggerModes = new[] { JobTriggerMode.Manual }
});
```

The runtime creates an [execution scope](../using-built-in-services.md#service-lifetime-and-ownership) and resolves the handler with its dependencies. In `IProgressReporter`, a null fraction means indeterminate progress, values from 0 to 1 describe completion, and `FlushAsync` waits for pending reports. The job can be run from **Run a job** once the extension is installed and Magpie restarted.

## Choosing execution behavior

`JobType` is a globally unique, stable identifier that triggers and execution records use, so prefix it with your extension's name. If two registrations share one, the host logs a warning and keeps the [last](README.md#registration-order), and existing triggers run that handler.

`Scope` is `User`, `Content`, `Administrative`, or `System`, and the host applies [role and ownership rules](../../reference/roles.md#job-permissions) to it. Do not use `System` for a job administrators schedule or trigger, because the host refuses to create, change, or delete triggers for a `System` job type.

`SupportedTriggerModes` declares manual, schedule, and event support. A manual run needs both `OnDemand = true` and `JobTriggerMode.Manual`. `RequesterTriggerModes` lets requesters own triggers in those modes, so limit it to operations that are safe for them. It does not grant administration access.

`SupportsImmediate` defaults to true and permits running outside the queue. **Run a job** uses immediate mode when the job allows it, and an immediate execution can overlap scheduled work. An immediate execution with the `Retry` failure policy is not retried, while `Requeue` applies the [job retry policy](job-retry-policy.md) in both modes.

`ConcurrencyScope` decides how executions wait for each other, and the default `PerType` runs one execution of the type at a time. `LockPartitionFields` names the parameters that form the lock partition for the target-based scopes, and `ConcurrencyLimit` sets how many executions of the type run at once. A job with a `ConcurrencyLimit` must set `SupportsImmediate = false`, or the host throws at startup. For a job that changes state, partition concurrency around the resource it changes, and remember that a retry can follow partial completion, so a repeated attempt must not repeat destructive work.

Read inputs from [`JobContext.Parameters`](../../../src/Magpie.Models/JobContext.cs) and event data from `JobContext.Event`, only when the specification supports them, and validate before changing anything. Pass the cancellation token on and let cancellation interrupt the work. An exception reports incomplete work and a normal return reports completion. `JobContext` also identifies the execution, its triggers, mode, and correlation chain. `ManualTriggerUserId` is the user who created the execution, or the system account for schedule and event runs.

## Running a job from an event or schedule

Registration declares support, and triggers are created separately. Include `JobTriggerMode.Event` in `SupportedTriggerModes` and read the matching event from `JobContext.Event`. After the extension loads, an administrator can [create an event trigger](../../administration/jobs-and-automation.md#creating-an-event-trigger) for the job. For work at set times, declare `JobTriggerMode.Schedule` and [create a schedule](../../administration/jobs-and-automation.md#creating-or-changing-a-schedule). Both run the handler in an execution scope through the same runtime.

See [IJob](../../../src/Magpie.Sdk/Jobs/IJob.cs), [JobSpec](../../../src/Magpie.Models/JobSpec.cs), and [JobContext](../../../src/Magpie.Models/JobContext.cs). Replacing the job runtime is [not supported](README.md#consumption-boundary).

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
