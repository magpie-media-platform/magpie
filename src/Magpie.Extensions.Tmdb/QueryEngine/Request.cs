using System.Collections.Generic;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record Request
{
    internal required string Endpoint { get; init; }

    internal required string Path { get; init; }

    internal required IReadOnlyList<(string Key, string Value)> QueryParameters { get; init; }

    internal required string CacheKey { get; init; }

    internal required ResponseProfile ResponseProfile { get; init; }
}
