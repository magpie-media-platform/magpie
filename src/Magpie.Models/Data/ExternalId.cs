using System;

namespace Magpie.Models.Data;

/// <summary>
/// An external id from a metadata provider.
/// </summary>
public record ExternalId
{
    /// <summary>
    /// The unique id of the external id record.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the metadata contribution that contains this external id.
    /// </summary>
    public Guid? MetadataContributionId { get; set; }

    /// <summary>
    /// The id of the release.
    /// </summary>
    public Guid? ReleaseId { get; set; }

    /// <summary>
    /// The position in the display order.
    /// </summary>
    public int Ordinal { get; set; }

    /// <summary>
    /// The value of the external id.
    /// </summary>
    public required string Value { get; set; }

    /// <summary>
    /// The name of the source that supplies the external id.
    /// </summary>
    public string? Source { get; set; }
}
