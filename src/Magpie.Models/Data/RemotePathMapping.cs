using System;

namespace Magpie.Models.Data;

/// <summary>
/// A map from a path on a remote host to a local path.
/// </summary>
public record RemotePathMapping
{
    /// <summary>
    /// The unique id of the remote path mapping.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The name of the remote host that reports the path.
    /// </summary>
    public required string Host { get; set; }

    /// <summary>
    /// The path prefix as the remote host reports it.
    /// </summary>
    public required string RemotePath { get; set; }

    /// <summary>
    /// The path prefix that replaces the remote prefix.
    /// </summary>
    public required string LocalPath { get; set; }

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
