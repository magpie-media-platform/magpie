using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.MetadataSearch;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed class SearchEngine(IQueryPlanner planner, IQueryExecutor executor) : ISearchEngine
{
    public async Task<QueryResult> SearchAsync(QueryRequest request, CancellationToken cancellationToken)
    {
        var plan = planner.Plan(
            request.EntityKind,
            request.Criteria,
            request.Page,
            request.OrdinaryTerm,
            request.MaximumResults);
        if (plan.CriterionDispositions.Any(value => value.Disposition == CriterionDispositionKind.Refused))
        {
            return new QueryResult
            {
                Supported = true,
                Warnings = [],
                PageInfo = new ProviderSearchPageInfo
                {
                    RequestedPage = request.Page,
                    HasMore = false
                }
            };
        }

        return await executor.SearchAsync(plan, request.Context, cancellationToken).ConfigureAwait(false);
    }
}
