using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed class QueryTelemetry(Activity? activity)
{
    private readonly object _gate = new();
    private readonly HashSet<int> _visitedNodes = [];
    private int _requests;
    private int _rowsFetched;
    private int _prunedNodes;

    internal Activity? Activity { get; } = activity;

    internal int Requests => Volatile.Read(ref _requests);

    internal int RowsFetched => Volatile.Read(ref _rowsFetched);

    internal int PrunedNodes => Volatile.Read(ref _prunedNodes);

    internal void RecordRequest(int rows)
    {
        Interlocked.Increment(ref _requests);
        Interlocked.Add(ref _rowsFetched, rows);
    }

    internal void RecordPrunedNode()
    {
        Interlocked.Increment(ref _prunedNodes);
    }

    internal bool TryVisitNode(int nodeId)
    {
        lock (_gate)
        {
            return _visitedNodes.Add(nodeId);
        }
    }
}
