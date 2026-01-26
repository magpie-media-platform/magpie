using System;

namespace Magpie.Models.Metadata;

/// <summary>
/// A materialized metadata result with its container identifier.
/// </summary>
/// <param name="ContainerId">The metadata container identifier.</param>
/// <param name="Metadata">The materialized metadata.</param>
public sealed record MaterializedMetadataResult(
    Guid ContainerId,
    MaterializedMetadata Metadata);
