namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed class QueryExecutionDiagnostics
{
    internal bool StreamFaulted { get; set; }

    internal bool ReceivedResponse { get; set; }
}
