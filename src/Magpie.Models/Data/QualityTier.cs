using System;

namespace Magpie.Models.Data;

/// <summary>
/// A quality tier within a quality tier list.
/// </summary>
public record QualityTier
{
    /// <summary>
    /// The quality tier id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The display name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The quality criteria used to match releases to this tier.
    /// </summary>
    /// <remarks>
    /// Serialized as JSON in the database.
    /// </remarks>
    public required QualityTierCriteria Criteria { get; set; }

    /// <summary>
    /// When the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user that created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// When the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user that last modified the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }
}
