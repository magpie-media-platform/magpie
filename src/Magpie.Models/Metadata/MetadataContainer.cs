using System;
using System.Collections.Generic;

namespace Magpie.Models.Metadata;

/// <summary>
/// A metadata container with layered contributions from providers.
/// </summary>
/// <remarks>
/// Contributions are ordered by <see cref="MetadataContribution.Ordinal"/>, where lower values
/// take precedence in conflicts. A contribution can be locked to prevent modification or removal via
/// <see cref="MetadataContribution.Locked"/>. When materialized to <see cref="MaterializedMetadata"/>,
/// scalar fields use the first non-null value by ordinal, and collection fields are merged in
/// ordinal order with deduplication.
/// </remarks>
/// <param name="Id">The unique identifier.</param>
/// <param name="DateAdded">The date the metadata was added.</param>
/// <param name="Contributions">The metadata contributions from providers, ordered by ordinal.</param>
public sealed record MetadataContainer(
    Guid Id,
    DateTimeOffset DateAdded,
    IReadOnlyList<MetadataContribution> Contributions);
