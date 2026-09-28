using System.Collections.Generic;
using System.Linq;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record QueryProgress
{
    internal string? Configuration { get; set; }

    internal string? PlanSignature { get; set; }

    internal string? ResolvedSources { get; set; }

    internal Dictionary<int, SourceProgress> Sources { get; set; } = [];

    internal Dictionary<int, List<SearchHit>> PendingIntersections { get; set; } = [];

    internal Dictionary<int, List<SearchHit>> Evaluation { get; set; } = [];

    internal HashSet<int> CompleteEvaluations { get; set; } = [];

    internal QueryProgress Copy() => new()
    {
        Configuration = Configuration,
        PlanSignature = PlanSignature,
        ResolvedSources = ResolvedSources,
        Sources = Sources.ToDictionary(pair => pair.Key, pair => pair.Value with
        {
            Seen = [.. pair.Value.Seen],
            ExhaustedSources = [.. pair.Value.ExhaustedSources]
        }),
        PendingIntersections = PendingIntersections.ToDictionary(pair => pair.Key, pair => pair.Value.ToList()),
        Evaluation = Evaluation.ToDictionary(pair => pair.Key, pair => pair.Value.ToList()),
        CompleteEvaluations = [.. CompleteEvaluations]
    };
}
