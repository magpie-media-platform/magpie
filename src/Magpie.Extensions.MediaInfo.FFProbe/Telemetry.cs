using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Magpie.Extensions.MediaInfo.FFProbe;

internal static class Telemetry
{
    internal const string Name = "magpie.extensions.ffprobe";

    internal static readonly string Version =
        typeof(Telemetry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
        typeof(Telemetry).Assembly.GetName().Version?.ToString(3) ??
        "0.0.0";

    internal static readonly ActivitySource Source = new(Name, Version);

    internal static readonly Meter Meter = new(Name, Version);

    internal static readonly Counter<long> Invocations = Meter.CreateCounter<long>(
        "magpie.mediainfo.ffprobe.invocations");

    internal static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "magpie.mediainfo.ffprobe.duration",
        unit: "ms");
}
