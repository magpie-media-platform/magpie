using System.Collections.Generic;

namespace Magpie.Extensions.Wikidata;

internal sealed record RequestContext
{
    public required string Language { get; init; }

    public required IReadOnlyList<string> FallbackLanguages { get; init; }

    public bool FetchAllLanguages { get; init; }

    public required string UserAgent { get; init; }
}
