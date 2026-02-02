using System;

namespace Magpie.Models.Data;

/// <summary>
/// An award or nomination associated with content.
/// </summary>
public record Award
{
    /// <summary>
    /// The award id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the metadata contribution this award belongs to.
    /// </summary>
    public Guid MetadataContributionId { get; set; }

    /// <summary>
    /// The display order.
    /// </summary>
    public int Ordinal { get; set; }

    /// <summary>
    /// The organization that grants the award (e.g., "Academy Awards", "Emmy Awards").
    /// </summary>
    public required string AwardBody { get; set; }

    /// <summary>
    /// The name of the award.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The award category.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// The status (e.g., "Won", "Nominated").
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// The year the award was given or nominated.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// The language of the award.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// The country associated with the award.
    /// </summary>
    public string? Country { get; set; }
}
