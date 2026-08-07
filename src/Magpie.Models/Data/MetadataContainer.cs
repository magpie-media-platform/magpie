using System;
using System.Collections.Generic;

namespace Magpie.Models.Data;

/// <summary>
/// A holder for the layered metadata contributions of one item.
/// </summary>
public record MetadataContainer
{
    /// <summary>
    /// The unique id of the metadata container.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The UTC time when the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The UTC time of the last refresh from the providers.
    /// </summary>
    public DateTime? DateLastRefreshed { get; set; }

    /// <summary>
    /// The provider contributions that the container holds.
    /// </summary>
    public ICollection<MetadataContribution> Contributions { get; } = [];
}
