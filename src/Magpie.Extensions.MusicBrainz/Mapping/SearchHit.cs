using System;
using System.Collections.Generic;

using Magpie.Extensions.MusicBrainz.QueryEngine;
using Magpie.Models.Filtering;

namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record SearchHit
{
    public required QueryTarget Target { get; init; }

    public required string PrimaryMbid { get; init; }

    public string? Title { get; init; }

    public string? SortTitle { get; init; }

    public string? Disambiguation { get; init; }

    public IReadOnlyList<AliasDto>? Aliases { get; init; }

    public string? ArtistCredit { get; init; }

    public string? ArtistType { get; init; }

    public IReadOnlyList<ArtistReference> ArtistReferences { get; init; } = [];

    public IReadOnlyList<LabelReference>? LabelReferences { get; init; }

    public IReadOnlyList<RelationDto>? UrlRelations { get; init; }

    public TemporalValue? FirstReleaseDate { get; init; }

    public TimeSpan? Duration { get; init; }

    public TemporalValue? Established { get; init; }

    public TemporalValue? Dissolved { get; init; }

    public TemporalValue? BirthDate { get; init; }

    public TemporalValue? DeathDate { get; init; }

    public bool? Ended { get; init; }

    public IReadOnlyList<string>? Languages { get; init; }

    public string? RepresentativeLanguage { get; init; }

    public IReadOnlyList<string>? Genres { get; init; }

    public IReadOnlyList<ExternalIdentifier> ExternalIds { get; init; } = [];
}
