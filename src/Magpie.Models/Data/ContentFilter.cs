using System;

namespace Magpie.Models.Data;

/// <summary>
/// A saved filter for content items.
/// </summary>
public record ContentFilter
{
    /// <summary>
    /// The unique id of the content filter.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the user who owns the content filter.
    /// </summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>
    /// The display name of the content filter.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The criteria of the content filter.
    /// </summary>
    public required ContentFilterCriteria Criteria { get; set; }

    /// <summary>
    /// Whether only the owner can edit the content filter.
    /// </summary>
    public bool? OwnerLock { get; set; }

    /// <summary>
    /// The UTC time the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user who created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// The UTC time the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user who last modified the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }
}
