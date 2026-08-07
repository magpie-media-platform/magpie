using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.MusicBrainz.Mapping;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal interface IQueryExecutor
{
    Task<QueryResult> ExecuteAsync(QueryPlan plan, CancellationToken cancellationToken);

    Task<FetchResult<IReadOnlyList<LabelReference>>> GetRepresentativeReleaseLabelsAsync(
        SearchHit hit,
        Settings settings,
        CancellationToken cancellationToken);
}
