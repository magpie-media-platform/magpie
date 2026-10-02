using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Magpie.Core.Observability;

internal static class MediaServerTelemetry
{
    internal const string Name = "magpie.core.media_servers";

    internal static readonly ActivitySource Source = new(Name, TelemetryVersion.Value);

    internal static readonly Meter Meter = new(Name, TelemetryVersion.Value);

    internal static readonly Counter<long> Refreshes = Meter.CreateCounter<long>("magpie.media_servers.refreshes");

    internal static readonly Histogram<double> RefreshDuration = Meter.CreateHistogram<double>(
        "magpie.media_servers.refresh.duration",
        unit: "ms");
}
