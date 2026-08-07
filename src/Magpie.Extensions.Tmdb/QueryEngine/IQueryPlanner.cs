namespace Magpie.Extensions.Tmdb.QueryEngine;

internal interface IQueryPlanner
{
    QueryPlan Plan(QueryRequest request);
}
