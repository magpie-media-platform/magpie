using System.Collections.Generic;
using System.Linq;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal static class PlanNodeExtensions
{
    internal static IReadOnlyList<PlanNode> Children(this PlanNode node)
    {
        return node switch
        {
            FetchPlanNode fetch => FetchChildren(fetch),
            FilterPlanNode filter => [filter.Child, .. filter.Exclusions],
            UnionPlanNode union => union.Children,
            IntersectPlanNode intersect => intersect.Children,
            _ => []
        };
    }

    internal static IReadOnlyList<PlanNode> Flatten(this PlanNode root)
    {
        return [root, .. root.Children().SelectMany(child => child.Flatten())];
    }

    private static IReadOnlyList<PlanNode> FetchChildren(FetchPlanNode fetch)
    {
        var children = fetch.Resolvers.Cast<PlanNode>().ToList();
        if (fetch.AnchorSource is not null)
        {
            children.Add(fetch.AnchorSource);
        }

        return children;
    }
}
