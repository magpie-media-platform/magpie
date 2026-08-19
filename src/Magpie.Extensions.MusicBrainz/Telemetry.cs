using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Magpie.Extensions.MusicBrainz;

internal static class Telemetry
{
    internal const string Name = "magpie.extensions.musicbrainz";

    internal static readonly string Version =
        typeof(Telemetry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
        typeof(Telemetry).Assembly.GetName().Version?.ToString(3) ??
        "0.0.0";

    internal static readonly ActivitySource Source = new(Name, Version);

    internal static readonly Meter Meter = new(Name, Version);

    internal static readonly KeyValuePair<string, object?> Provider = new("provider", Constants.ProviderType);

    internal static readonly KeyValuePair<string, object?> ContentType = new(
        "content_type",
        Constants.ContentTypeSlug);

    internal static readonly Counter<long> Plans = Meter.CreateCounter<long>("magpie.query.plans");

    internal static readonly Counter<long> PlansRefused = Meter.CreateCounter<long>("magpie.query.plans_refused");

    internal static readonly Histogram<double> QueryDuration = Meter.CreateHistogram<double>(
        "magpie.query.duration",
        unit: "ms");

    internal static readonly Counter<long> Requests = Meter.CreateCounter<long>("magpie.query.requests");

    internal static readonly Counter<long> Cache = Meter.CreateCounter<long>("magpie.query.cache");

    internal static readonly Histogram<int> Results = Meter.CreateHistogram<int>("magpie.query.results");
}
