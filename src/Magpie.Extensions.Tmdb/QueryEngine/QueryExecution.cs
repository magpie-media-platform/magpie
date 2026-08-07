using System.Collections.Generic;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record QueryExecution
{
    internal required bool Supported { get; init; }

    internal required IAsyncEnumerable<SearchHit> Hits { get; init; }

    internal required List<string> Warnings { get; init; }

    internal required int RequestedPage { get; init; }

    internal required QueryPagingMode PagingMode { get; init; }

    internal bool SourceHasMore { get; init; }

    internal QueryExecutionDiagnostics Diagnostics { get; init; } = new();
}
