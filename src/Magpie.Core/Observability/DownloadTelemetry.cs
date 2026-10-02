using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Magpie.Core.Observability;

internal static class DownloadTelemetry
{
    internal const string Name = "magpie.core.downloads";

    internal static readonly ActivitySource Source = new(Name, TelemetryVersion.Value);

    internal static readonly Meter Meter = new(Name, TelemetryVersion.Value);

    internal static readonly Counter<long> Transitions = Meter.CreateCounter<long>("magpie.downloads.transitions");

    internal static readonly Counter<long> Failures = Meter.CreateCounter<long>("magpie.downloads.failures");

    internal static readonly Counter<long> StuckDetections = Meter.CreateCounter<long>(
        "magpie.downloads.stuck_detections");

    internal static readonly Counter<long> Imports = Meter.CreateCounter<long>("magpie.downloads.imports");

    internal static readonly Histogram<long> ImportBytes = Meter.CreateHistogram<long>(
        "magpie.downloads.import.bytes",
        unit: "By");

    internal static readonly Histogram<int> ImportFiles = Meter.CreateHistogram<int>("magpie.downloads.import.files");

    internal static readonly Counter<long> ImportBlockedFiles = Meter.CreateCounter<long>(
        "magpie.downloads.import.blocked_files");
}
