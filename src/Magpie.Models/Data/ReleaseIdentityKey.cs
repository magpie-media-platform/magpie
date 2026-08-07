using System;

namespace Magpie.Models.Data;

/// <summary>
/// A provider-agnostic identity key for a release or a download.
/// </summary>
public record ReleaseIdentityKey
{
    /// <summary>
    /// The unique id of the release identity key.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the release that the key identifies.
    /// </summary>
    public Guid? ReleaseId { get; set; }

    /// <summary>
    /// The id of the download that the key identifies.
    /// </summary>
    public Guid? DownloadId { get; set; }

    /// <summary>
    /// The position in display order.
    /// </summary>
    public int Ordinal { get; set; }

    /// <summary>
    /// The name of the authority that defines the key.
    /// </summary>
    public required string Authority { get; set; }

    /// <summary>
    /// The key value at the authority.
    /// </summary>
    public required string Value { get; set; }

    /// <summary>
    /// The <see cref="Release"/> that the key identifies.
    /// </summary>
    public Release? Release { get; set; }

    /// <summary>
    /// The <see cref="Download"/> that the key identifies.
    /// </summary>
    public Download? Download { get; set; }
}
