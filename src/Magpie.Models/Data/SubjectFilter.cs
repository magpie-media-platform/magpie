using System;

namespace Magpie.Models.Data;

/// <summary>
/// A saved filter for subjects.
/// </summary>
public record SubjectFilter
{
    /// <summary>
    /// The subject filter id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the user that owns the subject filter, if applicable.
    /// </summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>
    /// The display name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The subject filter criteria.
    /// </summary>
    /// <remarks>
    /// Serialized as JSON in the database.
    /// </remarks>
    public required SubjectFilterCriteria Criteria { get; set; }

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
