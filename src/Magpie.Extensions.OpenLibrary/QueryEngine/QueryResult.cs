using System.Collections.Generic;

using Magpie.Models.MetadataSearch;

namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal sealed record QueryResult
{
    public required IReadOnlyList<WorkHit> Hits { get; init; }

    public required ProviderSearchPageInfo PageInfo { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IReadOnlyList<string> CandidateLanguagePreferences { get; init; } = [];
}
