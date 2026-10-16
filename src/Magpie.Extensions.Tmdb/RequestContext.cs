using Magpie.Extensions.Tmdb.QueryEngine;

namespace Magpie.Extensions.Tmdb;

internal sealed record RequestContext
{
    internal QueryProgress? Progress { get; init; }

    internal bool ValidateOnly { get; init; }

    internal required string ApiKey { get; init; }

    internal required string Language { get; init; }

    internal QueryTelemetry? Telemetry { get; init; }

    internal QueryExecutionDiagnostics? Diagnostics { get; init; }

    internal int? PlanNodeId { get; init; }
}
