using System;
using System.Collections.Generic;

using Magpie.Extensions.MusicBrainz.Mapping;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record ResidualEntityData
{
    public IReadOnlyList<AliasDto>? Aliases { get; init; }

    public IReadOnlyList<RelationDto>? UrlRelations { get; init; }

    public IReadOnlyList<string>? Genres { get; init; }

    public string? Language { get; init; }

    public TimeSpan? Duration { get; init; }

    public IReadOnlyList<ExternalIdentifier>? ExternalIds { get; init; }
}
