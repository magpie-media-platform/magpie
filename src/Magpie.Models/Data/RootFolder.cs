using System;

namespace Magpie.Models.Data;

/// <summary>
/// A root folder for storing content.
/// </summary>
public record RootFolder
{
    /// <summary>
    /// The root folder id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The display name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The filesystem path.
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// The content type this folder stores.
    /// </summary>
    public Guid ContentTypeId { get; set; }

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
