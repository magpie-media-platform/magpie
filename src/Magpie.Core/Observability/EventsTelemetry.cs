using System.Diagnostics.Metrics;

using Magpie.Core.Events;

namespace Magpie.Core.Observability;

internal static class EventsTelemetry
{
    internal const string Name = "magpie.core.events";

    internal static readonly Meter Meter = new(Name, TelemetryVersion.Value);

    internal static readonly Counter<long> PublishedTotal = Meter.CreateCounter<long>("magpie.events.published_total");

    internal static readonly Histogram<double> DispatchLatency = Meter.CreateHistogram<double>(
        "magpie.events.dispatch_latency",
        unit: "ms");

    internal static readonly ObservableGauge<long> ListenerChannelDepthGauge = Meter.CreateObservableGauge(
        "magpie.events.listener_channel_depth",
        EventListener.MeasureChannelDepths);
}
