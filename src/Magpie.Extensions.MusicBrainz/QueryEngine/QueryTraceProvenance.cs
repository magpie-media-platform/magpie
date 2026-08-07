using System;
using System.Collections.Generic;
using System.Linq;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record QueryTraceProvenance
{
    [Flags]
    public enum Disposition
    {
        None = 0,

        RemoteFilter = 1 << 0,

        ReleaseIndexFilter = 1 << 1,

        LocalFilter = 1 << 2,

        DirectLookup = 1 << 3,

        Residual = 1 << 4,

        Composition = 1 << 5,

        PlannedEmpty = 1 << 6,

        Refused = 1 << 7,

        Vacuous = 1 << 8,

        SuppressedByPlannedEmpty = 1 << 9,

        SuppressedByNoBoundedSeed = 1 << 10
    }

    public IReadOnlyList<Criterion> Criteria { get; init; } = [];

    internal bool Includes(Disposition disposition)
    {
        return Criteria.Any(criterion => (criterion.FinalDisposition & disposition) != 0);
    }

    public sealed record Criterion
    {
        public required string Source { get; init; }

        public required Disposition FinalDisposition { get; init; }
    }
}
