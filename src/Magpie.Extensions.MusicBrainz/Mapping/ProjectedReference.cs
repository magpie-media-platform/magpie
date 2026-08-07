using System;

namespace Magpie.Extensions.MusicBrainz.Mapping;

internal sealed record ProjectedReference
{
    public required string Kind { get; init; }

    public required Uri Url { get; init; }
}
