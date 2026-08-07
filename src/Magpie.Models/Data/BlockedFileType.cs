using System;

namespace Magpie.Models.Data;

/// <summary>
/// One entry in the deny list of file types.
/// </summary>
public sealed record BlockedFileType
{
    /// <summary>
    /// The unique id of the blocked file type.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The blocked file extension or MIME pattern.
    /// </summary>
    public required string Pattern { get; set; }

    /// <summary>
    /// The UTC time the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The UTC time the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user who created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// The id of the user who last modified the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }
}
