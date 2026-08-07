using System.Collections.Generic;

namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal sealed record ApiRequest
{
    public required ApiRequestKind Kind { get; init; }

    public required string Path { get; init; }

    public required IReadOnlyList<ApiRequestParameter> Parameters { get; init; }

    public required string CacheKey { get; init; }
}
