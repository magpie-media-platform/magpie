using System;
using System.Collections.Generic;

using Magpie.Models.Filtering;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed record SearchHit
{
    public required string Qid { get; init; }

    public required string TypeQid { get; init; }

    public required string Label { get; init; }

    public string? Description { get; init; }

    public TemporalValue? PublishDate { get; init; }

    public TemporalValue? EndDate { get; init; }

    public bool HasEndDate { get; init; }

    public TimeSpan? Duration { get; init; }

    public string? Language { get; init; }

    public string? PosterFilename { get; init; }

    public string? ImageFilename { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = [];

    public bool CandidateProjectionApplied { get; init; }

    public int? SearchOrdinal { get; init; }
}
