using Magpie.Models.Data;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record QueryRequest
{
    internal QueryProgress? Progress { get; init; }

    internal bool ValidateOnly { get; init; }

    internal required ContentRoute Route { get; init; }

    internal required MetadataCriteria Criteria { get; init; }

    internal required int Page { get; init; }

    internal int? MaximumResults { get; init; }

    internal required string DefaultLanguagePreference { get; init; }

    internal string? OrdinarySearchTerm { get; init; }
}
