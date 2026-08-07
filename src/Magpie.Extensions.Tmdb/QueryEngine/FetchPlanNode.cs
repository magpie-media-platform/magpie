using System.Collections.Generic;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record FetchPlanNode : PlanNode
{
    internal required ContentRoute Route { get; init; }

    internal required QueryAnchorKind AnchorKind { get; init; }

    internal required IReadOnlyList<SubjectResolutionBinding> SubjectBindings { get; init; }

    internal IReadOnlyList<SubjectRole> SubjectRoles { get; init; } = [];

    internal IReadOnlyList<SubjectRoleSelection> SubjectRoleSelections { get; init; } = [];

    internal IReadOnlyList<DiscoverParameter> DiscoverParameters { get; init; } = [];

    internal IReadOnlyList<TextFilter> TextFilters { get; init; } = [];

    internal IReadOnlyList<TemporalFilter> PublishDateFilters { get; init; } = [];

    internal IReadOnlyList<DurationFilter> DurationFilters { get; init; } = [];

    internal IReadOnlyList<GenreFilter> GenreFilters { get; init; } = [];

    internal IReadOnlyList<ContentIdFilter> ContentIdFilters { get; init; } = [];

    internal IReadOnlyList<ElementFilter> ElementFilters { get; init; } = [];

    internal bool IgnoreSubjectFilters { get; init; }

    internal required IReadOnlyList<string> PushedFilters { get; init; }

    internal string? OrdinarySearchTerm { get; init; }

    internal required int RequestedPage { get; init; }

    internal int? MaximumResults { get; init; }

    internal required string Fingerprint { get; init; }

    internal ResolvePlanNode? Resolve { get; init; }

    internal bool IsIntersectionBranch { get; init; }

    internal bool RequiresLogicalStream { get; init; }

    internal bool BranchScopedScan { get; init; }
}
