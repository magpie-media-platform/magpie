using System;
using System.Collections.Generic;

using Magpie.Extensions.Tmdb.Mapping;
using Magpie.Models;
using Magpie.Models.Data;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record SearchHit
{
    internal required ContentRoute Route { get; init; }

    internal required int Id { get; init; }

    internal string? Title { get; init; }

    internal string? OriginalTitle { get; init; }

    internal string? Overview { get; init; }

    internal string? OriginalLanguage { get; init; }

    internal DateTime? ReleaseDate { get; init; }

    internal TimeSpan? Duration { get; init; }

    internal string? ProductionStatus { get; init; }

    internal IReadOnlyList<int> GenreIds { get; init; } = [];

    internal IReadOnlyList<string> GenreNames { get; init; } = [];

    internal string? PosterPath { get; init; }

    internal string? BackdropPath { get; init; }

    internal string? Homepage { get; init; }

    internal ImagesPayloadDto? ImageGallery { get; init; }

    internal IReadOnlyList<AlternateText> AlternateTexts { get; init; } = [];

    internal IReadOnlyList<ContentRating> ContentRatings { get; init; } = [];

    internal IReadOnlyList<Preview> Previews { get; init; } = [];

    internal Uri? ImageUrl { get; init; }

    internal IReadOnlyDictionary<string, string> ExternalIds { get; init; } =
        new Dictionary<string, string>();

    internal IReadOnlyList<ProviderMetadataSearchSubjectReference> SubjectReferences { get; init; } = [];

    internal int SourceOrder { get; init; }

    internal int? SourcePlanNodeId { get; init; }

    internal bool DetailsLoaded { get; init; }

    internal DiscoverRequest? AppliedCriteria { get; init; }
}
