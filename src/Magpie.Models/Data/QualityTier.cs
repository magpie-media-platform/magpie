using System;
using System.Collections.Generic;

namespace Magpie.Models.Data;

/// <summary>
/// A named quality level that a release can match.
/// </summary>
public record QualityTier
{
    /// <summary>
    /// The unique id of the quality tier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The name of the tier to show.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The criteria that a release must match for the tier.
    /// </summary>
    public required QualityTierCriteria Criteria { get; set; }

    /// <summary>
    /// The accept and exclude rules that apply only to the tier.
    /// </summary>
    public List<QualityTierRule> Rules { get; set; } = [];

    /// <summary>
    /// The UTC time when the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user who created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// The UTC time when the record last changed.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user who last changed the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }
}
