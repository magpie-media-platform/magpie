using System;
using System.Collections.Generic;

namespace Magpie.Models.Data;

/// <summary>
/// A container for layered metadata contributions.
/// </summary>
public record MetadataContainer
{
    /// <summary>
    /// The metadata container id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// When the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the metadata was last refreshed.
    /// </summary>
    public DateTime? DateLastRefreshed { get; set; }

    /// <summary>
    /// Metadata contributions from various providers.
    /// </summary>
    public ICollection<MetadataContribution> Contributions { get; } = [];
}
