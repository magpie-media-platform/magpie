using System;
using System.Collections.Generic;

namespace Magpie.Extensions.Wikidata;

internal sealed record RequestContext
{
    internal Action? UpstreamRequestObserver { get; init; }

    internal QueryEngine.QueryProgress? Progress { get; init; }

    public required string Language { get; init; }

    public required IReadOnlyList<string> FallbackLanguages { get; init; }

    public bool FetchAllLanguages { get; init; }

    public required string UserAgent { get; init; }
}
