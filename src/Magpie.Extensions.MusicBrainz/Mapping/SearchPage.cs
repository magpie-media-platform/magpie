using System.Collections.Generic;

namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record SearchPage
{
    public int Count { get; init; }

    public int Offset { get; init; }

    public IReadOnlyList<ArtistDto> Artists { get; init; } = [];

    public IReadOnlyList<ReleaseGroupDto> ReleaseGroups { get; init; } = [];

    public IReadOnlyList<RecordingDto> Recordings { get; init; } = [];

    public IReadOnlyList<ReleaseDto> Releases { get; init; } = [];
}
