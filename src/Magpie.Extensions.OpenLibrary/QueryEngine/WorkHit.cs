using System.Collections.Generic;

namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal sealed record WorkHit
{
    public required string WorkOlid { get; init; }

    public required string Title { get; init; }

    public string? Subtitle { get; init; }

    public IReadOnlyList<string> AlternativeTitles { get; init; } = [];

    public required IReadOnlyList<AuthorPair> Authors { get; init; }

    public int? FirstPublishYear { get; init; }

    public IReadOnlyList<string> Languages { get; init; } = [];

    public IReadOnlyList<string> Subjects { get; init; } = [];

    public IReadOnlyList<string> SubjectKeys { get; init; } = [];

    public IReadOnlyList<string> Isbns { get; init; } = [];

    public long? CoverId { get; init; }

    public string? CoverEditionOlid { get; init; }

    public int? NumberOfPagesMedian { get; init; }

    public IReadOnlyList<string> Publishers { get; init; } = [];

    public string? Description { get; init; }

    public IReadOnlyList<SubjectFacetPair> Persons { get; init; } = [];

    public IReadOnlyList<SubjectFacetPair> Places { get; init; } = [];

    public IReadOnlyList<SubjectFacetPair> Times { get; init; } = [];

    public IReadOnlyList<string> PublishDates { get; init; } = [];

    public IReadOnlyList<string> Oclcs { get; init; } = [];

    public IReadOnlyList<string> Lccns { get; init; } = [];

    public IReadOnlyList<string> GoodreadsIds { get; init; } = [];
}
