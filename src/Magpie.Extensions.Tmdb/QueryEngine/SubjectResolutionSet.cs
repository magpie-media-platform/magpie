using System.Collections.Generic;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record SubjectResolutionSet
{
    internal required IReadOnlyList<ResolvedSubjectGroup> Groups { get; init; }

    internal required IReadOnlyList<string> Warnings { get; init; }

    internal required bool RequiredGroupUnresolved { get; init; }

    internal required bool HadRemoteFailure { get; init; }
}
