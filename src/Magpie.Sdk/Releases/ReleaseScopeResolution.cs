using System;
using System.Collections.Generic;

namespace Magpie.Sdk.Releases;

/// <summary>
/// The hierarchy nodes that a release scopes to and the actionable nodes in that scope.
/// </summary>
/// <param name="ScopeNodeIds">The ids of the hierarchy nodes that the release scopes to</param>
/// <param name="ActionableNodeIds">The ids of the lowest actionable nodes in the scope</param>
public sealed record ReleaseScopeResolution(
    IReadOnlyList<Guid> ScopeNodeIds,
    IReadOnlyList<Guid> ActionableNodeIds);
