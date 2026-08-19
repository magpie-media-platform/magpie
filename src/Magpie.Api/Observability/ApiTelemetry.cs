using System.Diagnostics.Metrics;
using System.Reflection;

namespace Magpie.Api.Observability;

internal static class ApiTelemetry
{
    internal const string Name = "magpie.api";

    internal static readonly string Version =
        typeof(ApiTelemetry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? typeof(ApiTelemetry).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";

    internal static readonly Meter Meter = new(Name, Version);

    internal static readonly UpDownCounter<long> SseActiveStreams = Meter.CreateUpDownCounter<long>(
        "magpie.api.sse.active_streams");

    internal static readonly Histogram<double> SseStreamDuration = Meter.CreateHistogram<double>(
        "magpie.api.sse.stream_duration",
        unit: "s");

    internal static readonly Counter<long> EventsDroppedByAllowlistTotal = Meter.CreateCounter<long>(
        "magpie.events.dropped_by_allowlist_total");
}
