using System;

namespace Magpie.Models.Data;

/// <summary>
/// A saved filter for content items.
/// </summary>
public record ContentFilter
{
    /// <summary>
    /// The content filter id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the user that owns the content filter, if applicable.
    /// </summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>
    /// The display name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The filter criteria.
    /// </summary>
    public required ContentFilterCriteria Criteria { get; set; }

    /// <summary>
    /// Whether the content filter is locked to and only editable by the owner.
    /// </summary>
    public bool? OwnerLock { get; set; }

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
