using System.Diagnostics;
using System.Reflection;

namespace Magpie.Extensions.Tvmaze;

internal static class Telemetry
{
    internal const string Name = "magpie.extensions.tvmaze";

    internal static readonly string Version =
        typeof(Telemetry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Telemetry).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";

    internal static readonly ActivitySource Source = new(Name, Version);
}
