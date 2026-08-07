using System.Collections.Generic;

using Magpie.Models.Filtering;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed record HierarchyRow
{
    public required EntityKind EntityKind { get; init; }

    public required string Qid { get; init; }

    public required int Ordinal { get; init; }

    public required string Title { get; init; }

    public string? Overview { get; init; }

    public TemporalValue? PublishDate { get; init; }

    public string? ImageFilename { get; init; }

    public IReadOnlyDictionary<string, string> ExternalIds { get; init; } = new Dictionary<string, string>();
}
