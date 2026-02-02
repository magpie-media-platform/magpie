using System;

namespace Magpie.Models.Data;

/// <summary>
/// A custom metadata key-value pair.
/// </summary>
public record CustomMetadataValue
{
    /// <summary>
    /// The custom metadata value id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The metadata contribution this value belongs to.
    /// </summary>
    public Guid MetadataContributionId { get; set; }

    /// <summary>
    /// The custom metadata field key.
    /// </summary>
    public required string Key { get; set; }

    /// <summary>
    /// The custom metadata field value.
    /// </summary>
    public string? Value { get; set; }
}
