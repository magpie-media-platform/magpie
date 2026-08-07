using Magpie.Models.Data;

namespace Magpie.Extensions.Tvmaze.Pipeline;

internal interface ISearchPlanner
{
    QueryPlan Plan(ContentHierarchyItemCriteria query, int maxResults);
}
