using System.Collections.Generic;
using System.Threading;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed class QueryScanState
{
    internal SemaphoreSlim Gate { get; } = new(1, 1);

    internal List<SearchHit> Hits { get; } = [];

    internal int NextSourcePage { get; set; } = 1;

    internal int NextSourceIndex { get; set; }

    internal HashSet<int> ExhaustedSourceIndexes { get; } = [];

    internal bool Exhausted { get; set; }
}
