using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Magpie.Extensions.Cardigann;

internal static class Telemetry
{
    internal const string Name = "magpie.extensions.cardigann";

    internal static readonly string Version =
        typeof(Telemetry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
        typeof(Telemetry).Assembly.GetName().Version?.ToString(3) ??
        "0.0.0";

    internal static readonly ActivitySource Source = new(Name, Version);

    internal static readonly Meter Meter = new(Name, Version);

    internal static readonly Counter<long> Logins = Meter.CreateCounter<long>("magpie.cardigann.logins");

    internal static readonly Counter<long> Solves = Meter.CreateCounter<long>("magpie.cardigann.solves");
}
