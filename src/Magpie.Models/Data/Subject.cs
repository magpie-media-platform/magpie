using System;

namespace Magpie.Models.Data;

/// <summary>
/// A subject that can be related to content (e.g., person, studio, publisher).
/// </summary>
public record Subject
{
    /// <summary>
    /// The subject id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the metadata container.
    /// </summary>
    public Guid MetadataContainerId { get; set; }

    /// <summary>
    /// The kind of subject (e.g., "person", "studio").
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>
    /// When metadata was last refreshed.
    /// </summary>
    public DateTime? DateLastRefreshed { get; set; }

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

    /// <summary>
    /// The <see cref="MetadataContainer"/> that contains metadata for this subject.
    /// </summary>
    public MetadataContainer? MetadataContainer { get; set; }
}
