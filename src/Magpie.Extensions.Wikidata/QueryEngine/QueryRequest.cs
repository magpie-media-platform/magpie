using Magpie.Models.Data;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed record QueryRequest
{
    public required EntityKind EntityKind { get; init; }

    public required MetadataCriteria Criteria { get; init; }

    public required RequestContext Context { get; init; }

    public required int Page { get; init; }

    public int MaximumResults { get; init; } = SparqlQueryBuilder.PageSize;

    public string? OrdinaryTerm { get; init; }
}
