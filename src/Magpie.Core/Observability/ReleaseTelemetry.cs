using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Magpie.Core.Observability;

internal static class ReleaseTelemetry
{
    internal const string Name = "magpie.core.releases";

    internal static readonly ActivitySource Source = new(Name, TelemetryVersion.Value);

    internal static readonly Meter Meter = new(Name, TelemetryVersion.Value);

    internal static readonly Counter<long> SearchTurns = Meter.CreateCounter<long>("magpie.releases.search.turns");

    internal static readonly Counter<long> SearchSkipped = Meter.CreateCounter<long>("magpie.releases.search.skipped");

    internal static readonly Counter<long> IndexerQueries = Meter.CreateCounter<long>("magpie.indexers.queries");

    internal static readonly Histogram<double> IndexerQueryDuration = Meter.CreateHistogram<double>(
        "magpie.indexers.query.duration",
        unit: "ms");

    internal static readonly Counter<long> IndexerQueryFailures = Meter.CreateCounter<long>(
        "magpie.indexers.query.failures");

    internal static readonly Counter<long> ReleasesEvaluated = Meter.CreateCounter<long>("magpie.releases.evaluated");

    internal static readonly Counter<long> QualityDecisions = Meter.CreateCounter<long>(
        "magpie.releases.quality_decisions");

    internal static readonly Counter<long> Grabs = Meter.CreateCounter<long>("magpie.releases.grabs");

    internal static readonly Counter<long> GrabTaints = Meter.CreateCounter<long>("magpie.releases.grab.taints");

    internal static readonly Counter<long> HealthTransitions = Meter.CreateCounter<long>(
        "magpie.indexers.health_transitions");
}
