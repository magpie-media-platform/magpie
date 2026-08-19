using System.Diagnostics.Metrics;
using System.Reflection;

namespace Magpie.Auth;

internal static class Telemetry
{
    internal const string Name = "magpie.auth";

    internal static readonly string Version =
        typeof(Telemetry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
        typeof(Telemetry).Assembly.GetName().Version?.ToString(3) ??
        "0.0.0";

    internal static readonly Meter Meter = new(Name, Version);

    internal static readonly Counter<long> LoginAttempts = Meter.CreateCounter<long>("magpie.auth.login_attempts");

    internal static readonly Counter<long> Lockouts = Meter.CreateCounter<long>("magpie.auth.lockouts");

    internal static readonly Counter<long> AuthenticationFailures = Meter.CreateCounter<long>(
        "magpie.auth.authentication_failures");

    internal static readonly Counter<long> TokensIssued = Meter.CreateCounter<long>("magpie.auth.tokens_issued");

    internal static readonly Counter<long> TokensRefreshed = Meter.CreateCounter<long>("magpie.auth.tokens_refreshed");
}
