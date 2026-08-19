using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Magpie.Core.Observability;

internal static class JobsTelemetry
{
    internal const string Name = "magpie.core.jobs";

    internal static readonly ActivitySource Source = new(Name, TelemetryVersion.Value);

    internal static readonly Meter Meter = new(Name, TelemetryVersion.Value);

    internal static readonly Histogram<double> ExecutionDuration = Meter.CreateHistogram<double>(
        "magpie.jobs.execution_duration",
        unit: "ms");

    internal static readonly Counter<long> FailuresTotal = Meter.CreateCounter<long>(
        "magpie.jobs.failures_total");

    internal static readonly Counter<long> RetriesTotal = Meter.CreateCounter<long>(
        "magpie.jobs.retries_total");

    internal static readonly Counter<long> RetryAttemptsExhaustedTotal = Meter.CreateCounter<long>(
        "magpie.jobs.retry_attempts_exhausted_total");

    internal static readonly Counter<long> MlfqDemotionsTotal = Meter.CreateCounter<long>(
        "magpie.jobs.mlfq_demotions_total");

    internal static readonly Histogram<double> FastLaneWaitTime = Meter.CreateHistogram<double>(
        "magpie.jobs.fast_lane_wait_time",
        unit: "ms");

    internal static readonly Histogram<double> AdmissionWaitTime = Meter.CreateHistogram<double>(
        "magpie.jobs.admission_wait_time",
        unit: "ms");

    internal static readonly Counter<long> OrphanReclaimsTotal = Meter.CreateCounter<long>(
        "magpie.jobs.orphan_reclaims_total");

    internal static readonly Histogram<double> ScheduleTriggerLag = Meter.CreateHistogram<double>(
        "magpie.jobs.schedule_trigger_lag",
        unit: "s");
}
