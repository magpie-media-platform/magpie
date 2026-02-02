using System;

using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="Data.Relationship"/>.
/// </summary>
/// <param name="Id">The id of the relationship to patch.</param>
/// <param name="RelatedMetadataContainerId">The related metadata container id patch.</param>
/// <param name="Ordinal">The ordinal patch.</param>
/// <param name="Kind">The kind patch.</param>
/// <param name="Qualifier">The qualifier patch.</param>
public sealed record RelationshipPatch(
    Guid Id,
    ScalarPatch<Guid>? RelatedMetadataContainerId = null,
    ScalarPatch<int>? Ordinal = null,
    ScalarPatch<string>? Kind = null,
    ScalarPatch<string?>? Qualifier = null);
