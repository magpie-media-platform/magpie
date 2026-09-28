using System.Collections.Generic;
using System.Linq;

using Magpie.Extensions.MusicBrainz.Mapping;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record QueryProgress
{
    internal bool ValidateOnly { get; set; }

    internal Dictionary<int, SourceProgress> Sources { get; set; } = [];

    internal HashSet<string> Delivered { get; set; } = [];

    internal Dictionary<string, SourceProgress> BrowseSources { get; set; } = [];

    internal Dictionary<int, List<SearchHit>> PendingIntersections { get; set; } = [];

    internal Dictionary<int, List<SearchHit>> Evaluation { get; set; } = [];

    internal HashSet<int> CompleteEvaluations { get; set; } = [];

    internal Dictionary<int, string> ResolverIdentities { get; set; } = [];

    internal Dictionary<int, string> ResolvedSources { get; set; } = [];

    internal QueryProgress Copy() => this with
    {
        Sources = Sources.ToDictionary(pair => pair.Key, pair => pair.Value with { Seen = [.. pair.Value.Seen] }),
        Delivered = [.. Delivered],
        BrowseSources = BrowseSources.ToDictionary(pair => pair.Key, pair => pair.Value with
        {
            Seen = [.. pair.Value.Seen]
        }),
        PendingIntersections = PendingIntersections.ToDictionary(pair => pair.Key, pair => pair.Value.ToList()),
        Evaluation = Evaluation.ToDictionary(pair => pair.Key, pair => pair.Value.ToList()),
        CompleteEvaluations = [.. CompleteEvaluations],
        ResolverIdentities = new Dictionary<int, string>(ResolverIdentities),
        ResolvedSources = new Dictionary<int, string>(ResolvedSources)
    };
}
