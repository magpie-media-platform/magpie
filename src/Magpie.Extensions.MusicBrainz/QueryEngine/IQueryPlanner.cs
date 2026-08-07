namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal interface IQueryPlanner
{
    QueryPlan Plan(QueryRequest request);
}
