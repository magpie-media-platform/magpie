namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal interface IQueryPlanner
{
    QueryPlan Plan(QueryRequest request);
}
