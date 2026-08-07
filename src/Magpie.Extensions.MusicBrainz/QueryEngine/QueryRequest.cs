using Magpie.Models.Data;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record QueryRequest
{
    public required QueryTarget Target { get; init; }

    public required MetadataCriteria Criteria { get; init; }

    public required int RequestedPage { get; init; }

    public int MaxResults { get; init; } = QueryLimits.DefaultMaxResults;

    public required Settings Settings { get; init; }

    public bool IsOrdinary { get; init; }

    public string? OrdinaryTerm { get; init; }
}
