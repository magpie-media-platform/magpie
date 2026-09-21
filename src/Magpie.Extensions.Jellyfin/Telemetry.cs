using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Magpie.Extensions.Jellyfin;

internal static class Telemetry
{
    internal const string Name = "magpie.extensions.jellyfin";

    internal static readonly string Version =
        typeof(Telemetry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
        typeof(Telemetry).Assembly.GetName().Version?.ToString(3) ??
        "0.0.0";

    internal static readonly Meter Meter = new(Name, Version);

    internal static readonly KeyValuePair<string, object?> ProviderType = new(
        "provider_type",
        Constants.ProviderTypeName);

    internal static readonly Counter<long> DeepLinkLookups = Meter.CreateCounter<long>(
        "magpie.media_servers.deep_link_lookups");
}
