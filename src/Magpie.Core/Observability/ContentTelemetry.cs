using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Magpie.Core.Observability;

internal static class ContentTelemetry
{
    internal const string Name = "magpie.core.content";

    internal static readonly ActivitySource Source = new(Name, TelemetryVersion.Value);

    internal static readonly Meter Meter = new(Name, TelemetryVersion.Value);

    internal static readonly Histogram<double> SearchDuration = Meter.CreateHistogram<double>(
        "magpie.metadata.search.duration",
        unit: "ms");

    internal static readonly Histogram<double> RefreshDuration = Meter.CreateHistogram<double>(
        "magpie.metadata.refresh.duration",
        unit: "ms");

    internal static readonly Histogram<int> RefreshCascadeChildren = Meter.CreateHistogram<int>(
        "magpie.metadata.refresh.cascade_children");
}
