using System.Collections.Generic;

using Magpie.Models.Data;

namespace Magpie.Models.Releases;

/// <summary>
/// One release of a query plan together with the outcome of its assessment.
/// </summary>
/// <param name="Release">The release that a connection returned</param>
/// <param name="ReleaseFacts">The facts that the parser read from the release</param>
/// <param name="Assessment">The assessment of the release against the target content item</param>
/// <param name="PlanNodeIds">The ids of the plan nodes that returned the release</param>
public sealed record EvaluatedRelease(
    Release Release,
    IReadOnlyList<ContentFact> ReleaseFacts,
    ReleaseAssessment Assessment,
    IReadOnlyList<string> PlanNodeIds);
