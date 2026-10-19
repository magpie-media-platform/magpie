# Telemetry reference

[Documentation](../README.md) / [Reference](README.md)

This page lists Magpie's telemetry export settings, resource attributes, metrics, and health endpoints. For investigating with them, see [Observability](../administration/observability.md).

**On this page**

- [Export and identity](#export-and-identity)
- [Metrics](#metrics)
- [Interpreting job and import metrics](#interpreting-job-and-import-metrics)
- [Collector verification](#collector-verification)
- [Health endpoints](#health-endpoints)

## Export and identity

[OTLP export](../administration/observability.md#telemetry-signals) is off by default. Setting `OTEL_EXPORTER_OTLP_ENDPOINT` to a receiver address on the Magpie process enables export of logs, metrics, and traces, and an unset, empty, or whitespace value disables it. `OTEL_EXPORTER_OTLP_PROTOCOL` selects the protocol of the receiver. For the [collector example](#collector-verification), use `grpc` and an endpoint such as `http://otel-collector:4317`. In Docker, `localhost` is the Magpie container, not a separate collector container.

| Resource attribute | Value |
| --- | --- |
| `service.name` | `magpie` |
| `service.version` | Application version |
| `magpie.node_id` | Configured node ID, otherwise the machine name |
| `magpie.clustered` | `true` when the configured [cluster mode](../administration/clustering.md) is not `SingleNode` |

Magpie exports activity sources and meters whose names start with `magpie.`. It also exports request, outgoing HTTP, database, resilience, and runtime telemetry. An [extension](../administration/extensions.md) can add instruments. Magpie exports them only when the meter or activity-source name starts with `magpie.`.

## Metrics

Metric names to select in a collector or monitoring system. The database [job history](../administration/jobs-and-automation.md#jobs-and-executions) remains the record of each execution.

### Jobs

Meter `magpie.core.jobs`.

| Metric | Type | Unit |
| --- | --- | --- |
| `magpie.jobs.execution_duration` | Histogram | Milliseconds |
| `magpie.jobs.failures_total` | Counter | Count |
| `magpie.jobs.retries_total` | Counter | Count |
| `magpie.jobs.retry_attempts_exhausted_total` | Counter | Count |
| `magpie.jobs.orphan_reclaims_total` | Counter | Count |
| `magpie.jobs.schedule_trigger_lag` | Histogram | Seconds |
| `magpie.jobs.queued` | Gauge | Executions |
| `magpie.jobs.deferred` | Gauge | Executions |
| `magpie.jobs.admission_wait_time` | Histogram | Milliseconds |
| `magpie.jobs.fast_lane_wait_time` | Histogram | Milliseconds |
| `magpie.jobs.fast_lane_in_use` | Gauge | Slots |
| `magpie.jobs.mlfq_level_occupancy` | Gauge | Executions |
| `magpie.jobs.mlfq_level_capacity` | Gauge | Slots |
| `magpie.jobs.mlfq_demotions_total` | Counter | Count |

The `mlfq` instruments describe the [scheduler levels](configuration.md#scheduler-levels) and have a `level` attribute. The fast lane holds the worker slots for immediate executions.

### Downloads, releases, and indexers

Meters `magpie.core.downloads` and `magpie.core.releases`.

| Metric | Type | Unit |
| --- | --- | --- |
| `magpie.downloads.transitions` | Counter | Count |
| `magpie.downloads.failures` | Counter | Count |
| `magpie.downloads.stuck_detections` | Counter | Count |
| `magpie.downloads.imports` | Counter | Count |
| `magpie.downloads.import.bytes` | Histogram | Bytes |
| `magpie.downloads.import.files` | Histogram | Files |
| `magpie.downloads.import.blocked_files` | Counter | Count |
| `magpie.releases.search.turns` | Counter | Count |
| `magpie.releases.search.skipped` | Counter | Count |
| `magpie.releases.evaluated` | Counter | Count |
| `magpie.releases.quality_decisions` | Counter | Count |
| `magpie.releases.grabs` | Counter | Count |
| `magpie.releases.grab.taints` | Counter | Count |
| `magpie.indexers.queries` | Counter | Count |
| `magpie.indexers.query.duration` | Histogram | Milliseconds |
| `magpie.indexers.query.failures` | Counter | Count |
| `magpie.indexers.health_transitions` | Counter | Count |

### Metadata, media servers, data, and events

Meters `magpie.core.content`, `magpie.core.media_servers`, `magpie.core.data`, and `magpie.core.events`.

| Metric | Type | Unit |
| --- | --- | --- |
| `magpie.metadata.search.duration` | Histogram | Milliseconds |
| `magpie.metadata.refresh.duration` | Histogram | Milliseconds |
| `magpie.metadata.refresh.cascade_children` | Histogram | Items |
| `magpie.media_servers.refreshes` | Counter | Count |
| `magpie.media_servers.refresh.duration` | Histogram | Milliseconds |
| `magpie.data.transaction.outcome` | Counter | Count |
| `magpie.data.transaction.duration` | Histogram | Milliseconds |
| `magpie.cache.hit` | Counter | Count |
| `magpie.cache.miss` | Counter | Count |
| `magpie.cache.refresh` | Counter | Count |
| `magpie.events.published_total` | Counter | Count |
| `magpie.events.dispatch_latency` | Histogram | Milliseconds |
| `magpie.events.listener_channel_depth` | Gauge | Events |

### API and authentication

Meters `magpie.api` and `magpie.auth`.

| Metric | Type | Unit |
| --- | --- | --- |
| `magpie.api.sse.active_streams` | Up-down counter | Streams |
| `magpie.api.sse.stream_duration` | Histogram | Seconds |
| `magpie.events.dropped_by_allowlist_total` | Counter | Count |
| `magpie.auth.login_attempts` | Counter | Count |
| `magpie.auth.lockouts` | Counter | Count |
| `magpie.auth.authentication_failures` | Counter | Count |
| `magpie.auth.tokens_issued` | Counter | Count |
| `magpie.auth.tokens_refreshed` | Counter | Count |

### First-party extensions

| Meter | Metrics |
| --- | --- |
| `magpie.extensions.tmdb`, `magpie.extensions.musicbrainz`, `magpie.extensions.openlibrary`, `magpie.extensions.wikidata` | `magpie.query.plans`, `magpie.query.plans_refused`, `magpie.query.requests`, and `magpie.query.cache` counters; `magpie.query.duration` histogram in milliseconds; `magpie.query.results` histogram of result counts |
| `magpie.extensions.cardigann` | `magpie.cardigann.logins` and `magpie.cardigann.solves` counters |
| `magpie.extensions.jellyfin` | `magpie.media_servers.deep_link_lookups` counter |
| `magpie.extensions.ffprobe` | `magpie.mediainfo.ffprobe.invocations` counter; `magpie.mediainfo.ffprobe.duration` histogram in milliseconds |

The four metadata extensions use the same metric names, distinguished by meter.

## Interpreting job and import metrics

- `magpie.jobs.execution_duration` measures the run time of an execution. It has the attributes `job.type` and `outcome`. It does not include the time in the queue.
- `magpie.jobs.schedule_trigger_lag` measures the delay between the expected run time of a schedule trigger and the time when the scheduler creates the execution. It does not include the run time.
- `magpie.jobs.orphan_reclaims_total` counts stale running executions that Magpie reclaimed.
- `magpie.jobs.queued` counts queued executions that can start now. `magpie.jobs.deferred` counts queued executions with a later start time, and its `retry` attribute is `true` for a retry and `false` for other delayed work. Both gauges have a `job_type` attribute and are reported only by nodes with the `JobRunner` role. Magpie counts them from the database at most every 15 seconds, and each job-runner node reports the same installation-wide counts, so do not add values across nodes.
- `magpie.downloads.imports` has an `outcome` attribute, such as `success`, `waiting`, or `failure`, and a `dry_run` attribute. To count completed imports, select `outcome=success` and `dry_run=false`.

## Collector verification

This collector configuration receives OTLP over gRPC and writes the received data to its output for a connection test:

```yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317
exporters:
  debug:
    verbosity: detailed
service:
  pipelines:
    traces:
      receivers: [otlp]
      exporters: [debug]
    metrics:
      receivers: [otlp]
      exporters: [debug]
    logs:
      receivers: [otlp]
      exporters: [debug]
```

Start an OpenTelemetry Collector distribution that includes these components with `otelcol --config=collector.yaml`, then set the Magpie export variables as in [Export and identity](#export-and-identity). The [detailed verbosity of the debug exporter](https://github.com/open-telemetry/opentelemetry-collector/blob/main/exporter/debugexporter/README.md#verbosity-levels) prints the resource attributes, so after loading a collection page the output should contain records with `service.name=magpie` and the node ID. Metrics can arrive at a different time from request and log records.

This receiver is for testing on a trusted network. A production collector needs destination exporters and access controls, which the [collector configuration reference](https://opentelemetry.io/docs/collector/configuration/) covers.

## Health endpoints

Both endpoints accept anonymous requests.

`GET /api/system/health` always returns HTTP 200. The `status` value in the body is `Migrating` while database migrations run, `StartupFailed` after a failed start, and `Healthy` after startup.

`GET /api/system/ready` returns HTTP 503 while the node starts. It also returns HTTP 503 when the database schema is newer than the release on this node. That node needs an upgrade. After a successful start, it returns HTTP 200.

Readiness shows whether the process can receive requests. Neither endpoint tests setup completion or integrations. For those, use [Doctor](../administration/doctor.md).

---

[Reference](README.md) | [Documentation](../README.md)
