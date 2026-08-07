using System.Collections.Generic;

namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal sealed record FilterAlternative
{
    public required IReadOnlyList<QueryFilter> Filters { get; init; }

    public bool AlignAuthors { get; init; }

    public bool AlignPublishers { get; init; }

    public bool AlignTexts { get; init; }

    public bool AlignGenres { get; init; }

    public QueryField? AlignSubjectFacet { get; init; }
}
