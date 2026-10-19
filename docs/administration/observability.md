# Observability

[Documentation](../README.md) / [Administration](README.md)

Activity history shows what changed, [job history](jobs-and-automation.md#jobs-and-executions) shows the work that followed, and telemetry (logs, metrics, and traces) shows why that work failed or was slow.

## Telemetry signals

Magpie can send logs, metrics, and traces with the OpenTelemetry Protocol (OTLP) to a collector, which forwards them to the monitoring system you choose. Alert rules belong in that monitoring system.

## Tracing an operation

Begin in the **Activity** tab of a title or in **Admin > Activity Log** and note the time and the related execution ID. Find that execution under **Admin > System Jobs > History** to see its status, attempts, and **Node**, then read that node's logs for the same period. If you collect traces, search them for the execution ID.

Note the identifiers before deleting history, because the **Retention cleanup schedule** removes finished executions after 30 days and audit records after 90 by default.

## Reading host logs

| Deployment | Log source |
| --- | --- |
| Docker | Container output, for example `docker logs --tail 100 magpie` |
| Kubernetes | Pod output, from the pod of the execution's node |
| Linux systemd service | The service journal |
| Windows MSI service | Windows Application event log, source `magpie` |
| Foreground native process | The terminal |

For a systemd service, `journalctl --unit magpie --since "1 hour ago"` shows recent output. For Kubernetes, `kubectl logs POD_NAME --tail=100` does, and `--previous` shows the output of a container that restarted.

The first relevant error and any inner exception usually explain later errors.

### Adjusting detail for an investigation

Host logging is configured through `Logging:LogLevel`, and the Windows Event Log has its own provider setting that defaults to Warning. This example adds job detail to both:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Magpie.Core.Jobs": "Debug"
    },
    "EventLog": {
      "LogLevel": {
        "Default": "Warning",
        "Magpie.Core.Jobs": "Debug"
      }
    }
  }
}
```

Restart after changing the host configuration, and set the levels back when you are done. Detailed logs are larger and can contain operational values. See [Windows EventLog provider](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/logging/?view=aspnetcore-10.0#windows-eventlog).

## Interpreting the data

Job activities carry `job.execution_id`, `job.correlation_id`, `job.parent_execution_id`, and `job.node_id`. Background work can appear as linked activities rather than one continuous trace, so search by execution ID or correlation ID.

| Observation | Meaning |
| --- | --- |
| Schedule-trigger lag rises | Executions start later than their trigger time. Check the scheduler and node health. This is not job runtime. |
| Duration rises for one job type | The work is slower once started. Look at traces for dependency or storage delays. |
| Failures and retries rise together | Some attempts fail and Magpie creates more. Check the final attempt. |
| Retry exhaustion rises | Executions hit the attempt limit. Fix the cause. |
| Orphan reclamations rise | Magpie reclaimed stale running work. Look at node restarts, connectivity, or worker health. |

To count completed imports that were not dry runs, select `magpie.downloads.imports` with `outcome=success` and `dry_run=false`. The [telemetry reference](../reference/telemetry.md) lists names, units, and resource attributes.

[Extensions](extensions.md) can write their own logs and use the `magpie.*` activity-source and meter prefix.

## Monitoring each node

In a [cluster](clustering.md), node IDs must be unique and stable. Filter by `magpie.node_id` to find failures on one host, and during a rollout check readiness and logs on each replica.

Exported records can contain request details, identifiers, errors, and log messages, so restrict access to the destination. Removing `OTEL_EXPORTER_OTLP_ENDPOINT` and restarting stops the export.

## Health and readiness

| Endpoint | Behavior |
| --- | --- |
| `/api/system/health` | Returns HTTP 200 with a status body, including during startup. The status can be `Migrating` or `StartupFailed`. |
| `/api/system/ready` | Returns HTTP 503 until the database work at startup completes, then HTTP 200. A node older than the database schema keeps returning 503. |

Both allow anonymous requests. Readiness does not check setup, service credentials, or media storage, which [Doctor](doctor.md), connection tests, and job results cover.

## Connecting an OTLP collector

The OTLP export is off by default. Set a nonempty `OTEL_EXPORTER_OTLP_ENDPOINT` to turn it on. [Host logs](#reading-host-logs) work without it.

To try it with a collector that prints what it receives, save the [collector example](../reference/telemetry.md#collector-verification) as `collector.yaml`, start it with `otelcol --config=collector.yaml`, and set these on Magpie before restarting:

```text
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
OTEL_EXPORTER_OTLP_PROTOCOL=grpc
```

The host name is an example. In Docker it can be the name of a collector container on a shared network, and in Kubernetes a Service address the pod can reach. `localhost` is the Magpie container or pod itself. To send data onward, see the [OpenTelemetry Collector configuration guide](https://opentelemetry.io/docs/collector/configuration/).

Confirm traces, logs, and metrics separately. Load a page, run a job, and look for records with service `magpie` and `magpie.node_id`. Metrics arrive after the export interval. If nothing arrives, check Magpie's environment, the receiver address and protocol, and the collector logs.

The example uses an unencrypted receiver on a trusted test network. Production use needs transport, credentials, and retention configured in the collector.

---

[Administration](README.md) | [Documentation](../README.md)
