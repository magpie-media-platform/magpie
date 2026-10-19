# Job retry policy

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

When a job fails, Magpie decides whether to try it again. `IJobRetryPolicy` is that decision, and you replace it to change how, when, or whether failed executions are retried. The runtime calls it when a job handler throws and the job's [failure policy](jobs.md#choosing-execution-behavior) is `Retry` or `Requeue`, but not for `Fail`, and not for `Retry` in immediate mode. The policy returns a new queued execution, or `null` to stop. The runtime persists what you return, so do not enqueue it yourself. Scheduling, ownership, and cancellation stay as they are, and replacing the job runtime is [not supported](README.md#consumption-boundary).

Register one [scoped](../using-built-in-services.md#service-lifetime-and-ownership) policy in [`IExtension.RegisterServices`](../../reference/extensions.md#entry-point). The host registers its default only when no extension does.

```csharp
services.AddScoped<IJobRetryPolicy, ExampleRetryPolicy>();
```

The replacement applies to the jobs of every extension and the host. Requeues after a host shutdown, a lost node, or a node restart always use the built-in policy, whatever the failure policy.

## The decision

`BuildRetryAsync` receives `failed`, the execution as the runtime loaded it before the run. It has neither the exception nor the failure message, so decide from the job type, parameters, and attempt count. A null `Attempt` means the first attempt. The runtime calls the policy inside the transaction that marks the execution failed. If the policy throws, that change rolls back, and the execution stays Running until stale-execution cleanup requeues it with the default policy.

This policy never retries:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Magpie.Models.Data;
using Magpie.Sdk.Jobs;

internal sealed class ExampleRetryPolicy : IJobRetryPolicy
{
    /// <inheritdoc />
    public Task<JobExecution?> BuildRetryAsync(
        JobExecution failed,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<JobExecution?>(null);
    }
}
```

## Returning another attempt

A retrying policy creates a new `JobExecution` that keeps the job type, extension identity, parameters, trigger context, scope, parent, correlation, trigger identifiers, and lock partition. Link it to the original with `OriginalExecutionId`, raise the attempt number, and set the earliest start with `NotBeforeAt`. Use queued execution mode. The failed attempt stays as a record, so do not reuse its execution ID or turn it into the new attempt. The default policy also adds the current [trace context](../../administration/observability.md#telemetry-signals) to `TriggerContext` when no propagated trace fields are present.

The [default policy](../../../src/Magpie.Core/Jobs/Runtime/DefaultJobRetryPolicy.cs) reads the `job-retry` settings group, with defaults of `base-seconds = 10`, `factor = 2`, `cap-seconds = 1800`, and `max-attempts = 5` including the first attempt. Another attempt does not fix bad credentials or a wrong destination path, so decide when your replacement stops.

See [IJobRetryPolicy](../../../src/Magpie.Sdk/Jobs/IJobRetryPolicy.cs).

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
