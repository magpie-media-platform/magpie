using System.Collections.Generic;

using Magpie.Models.Data;
using Magpie.Models.Releases;

namespace Magpie.Core.Releases;

internal sealed record EvaluatedRelease(
    Release Release,
    IReadOnlyList<ContentFact> ReleaseFacts,
    ReleaseAssessment Assessment,
    IReadOnlyList<string> PlanNodeIds);
