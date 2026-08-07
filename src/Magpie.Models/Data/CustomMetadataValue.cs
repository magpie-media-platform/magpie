using System;

namespace Magpie.Models.Data;

/// <summary>
/// A custom metadata key-value pair.
/// </summary>
public record CustomMetadataValue
{
    /// <summary>
    /// The unique id of the custom metadata value.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the metadata contribution that contains this value.
    /// </summary>
    public Guid MetadataContributionId { get; set; }

    /// <summary>
    /// The name of the custom metadata field.
    /// </summary>
    public required string Key { get; set; }

    /// <summary>
    /// The value of the custom metadata field.
    /// </summary>
    public string? Value { get; set; }
}
