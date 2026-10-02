using System.Diagnostics.Metrics;

namespace Magpie.Core.Observability;

internal static class DataTelemetry
{
    internal const string Name = "magpie.core.data";

    internal static readonly Meter Meter = new(Name, TelemetryVersion.Value);

    internal static readonly Counter<long> TransactionOutcome = Meter.CreateCounter<long>(
        "magpie.data.transaction.outcome");

    internal static readonly Histogram<double> TransactionDuration = Meter.CreateHistogram<double>(
        "magpie.data.transaction.duration",
        unit: "ms");

    internal static readonly Counter<long> CacheHit = Meter.CreateCounter<long>("magpie.cache.hit");

    internal static readonly Counter<long> CacheMiss = Meter.CreateCounter<long>("magpie.cache.miss");

    internal static readonly Counter<long> CacheRefresh = Meter.CreateCounter<long>("magpie.cache.refresh");
}
