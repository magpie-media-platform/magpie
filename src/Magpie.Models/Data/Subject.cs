using System;

namespace Magpie.Models.Data;

/// <summary>
/// An entity that content can link to.
/// </summary>
public record Subject
{
    /// <summary>
    /// The unique id of the subject.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the container that holds the metadata of the subject.
    /// </summary>
    public Guid MetadataContainerId { get; set; }

    /// <summary>
    /// The kind of subject, as an opaque slug.
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>
    /// The UTC time of the last refresh from the providers.
    /// </summary>
    public DateTime? DateLastRefreshed { get; set; }

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

    /// <summary>
    /// The <see cref="MetadataContainer"/> that holds the metadata of the subject.
    /// </summary>
    public MetadataContainer? MetadataContainer { get; set; }
}
