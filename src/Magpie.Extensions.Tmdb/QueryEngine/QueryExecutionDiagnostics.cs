namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed class QueryExecutionDiagnostics
{
    internal bool StreamFaulted { get; set; }

    internal ExecutionTrace? Trace { get; set; }

    internal QueryPlan? Plan { get; set; }

    internal long StartedTimestamp { get; set; }
}
