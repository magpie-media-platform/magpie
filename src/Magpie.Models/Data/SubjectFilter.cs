using System;

namespace Magpie.Models.Data;

/// <summary>
/// A saved filter for subjects.
/// </summary>
public record SubjectFilter
{
    /// <summary>
    /// The unique id of the subject filter.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the user who owns the filter.
    /// </summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>
    /// The name of the filter to show.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The criteria that a subject must match.
    /// </summary>
    public required SubjectFilterCriteria Criteria { get; set; }

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
