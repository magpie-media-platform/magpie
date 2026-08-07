using Magpie.Models.Data;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal interface IQueryPlanner
{
    QueryPlan Plan(
        EntityKind entityKind,
        MetadataCriteria criteria,
        int page,
        string? ordinaryTerm = null,
        int maximumResults = SparqlQueryBuilder.PageSize);
}
