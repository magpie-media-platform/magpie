using System;

namespace Magpie.Models.Data;

/// <summary>
/// An award or a nomination for content.
/// </summary>
public record Award
{
    /// <summary>
    /// The unique id of the award.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the metadata contribution that contains this award.
    /// </summary>
    public Guid MetadataContributionId { get; set; }

    /// <summary>
    /// The position in the display order.
    /// </summary>
    public int Ordinal { get; set; }

    /// <summary>
    /// The name of the body that gives the award.
    /// </summary>
    public required string Body { get; set; }

    /// <summary>
    /// The display name of the award.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The category of the award.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// The status of the award.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// The year of the award or the nomination.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// The language code of the award.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// The country code of the award.
    /// </summary>
    public string? Country { get; set; }
}
