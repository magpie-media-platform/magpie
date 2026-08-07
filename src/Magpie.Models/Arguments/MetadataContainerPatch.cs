using System;
using System.Collections.Generic;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for a <see cref="MetadataContainer"/>.
/// </summary>
public sealed record MetadataContainerPatch : MetadataContributionPatch
{
    /// <summary>
    /// The id of the metadata container whose user contribution to patch.
    /// </summary>
    public Guid ContainerId { get; init; }

    /// <summary>
    /// The patch for the primary image id.
    /// </summary>
    public ScalarPatch<Guid>? PrimaryImageId { get; init; }

    /// <summary>
    /// Every provider contribution id of the container, in precedence order.
    /// </summary>
    public IReadOnlyList<Guid>? ContributionIds { get; init; }
}
