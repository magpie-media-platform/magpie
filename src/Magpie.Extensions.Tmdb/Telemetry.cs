using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Magpie.Extensions.Tmdb;

internal static class Telemetry
{
    internal const string Name = "magpie.extensions.tmdb";

    internal const string Provider = "tmdb";

    internal static readonly string Version =
        typeof(Telemetry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? typeof(Telemetry).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";

    internal static readonly ActivitySource Source = new(Name, Version);

    internal static readonly Meter Meter = new(Name, Version);

    internal static readonly KeyValuePair<string, object?> ProviderTag = new("provider", Provider);

    internal static readonly KeyValuePair<string, object?> CacheHitTag = new("cache_hit", true);

    internal static readonly KeyValuePair<string, object?> CacheMissTag = new("cache_hit", false);

    internal static readonly KeyValuePair<string, object?> OutcomeHitTag = new("outcome", "hit");

    internal static readonly KeyValuePair<string, object?> OutcomeMissTag = new("outcome", "miss");

    internal static readonly Counter<long> Plans = Meter.CreateCounter<long>(
        "magpie.query.plans");

    internal static readonly Counter<long> PlansRefused = Meter.CreateCounter<long>(
        "magpie.query.plans_refused");

    internal static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "magpie.query.duration",
        unit: "ms");

    internal static readonly Counter<long> Requests = Meter.CreateCounter<long>(
        "magpie.query.requests");

    internal static readonly Counter<long> Cache = Meter.CreateCounter<long>(
        "magpie.query.cache");

    internal static readonly Histogram<int> Results = Meter.CreateHistogram<int>(
        "magpie.query.results");

    internal static Activity? StartChild(this ActivitySource source, string name, Activity? parent)
    {
        return parent is null
            ? source.StartActivity(name)
            : source.StartActivity(name, ActivityKind.Internal, parent.Context);
    }
}
