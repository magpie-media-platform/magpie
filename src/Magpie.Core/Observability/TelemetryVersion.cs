using System.Reflection;

namespace Magpie.Core.Observability;

internal static class TelemetryVersion
{
    internal static readonly string Value =
        typeof(TelemetryVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? typeof(TelemetryVersion).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";
}
