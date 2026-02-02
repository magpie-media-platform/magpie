using System;
using System.Collections.Generic;

namespace Magpie.Models.Data;

/// <summary>
/// A downloadable instance of a <see cref="ContentItem"/>.
/// </summary>
public record Release
{
    /// <summary>
    /// The release id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The indexer this release came from.
    /// </summary>
    public Guid? IndexerId { get; set; }

    /// <summary>
    /// The content item this release is associated with.
    /// </summary>
    public Guid? ContentItemId { get; set; }

    /// <summary>
    /// Protocol used to download the release.
    /// </summary>
    public required string Protocol { get; set; }

    /// <summary>
    /// The external release identifier from the indexer.
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// The release title.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// When the release was published.
    /// </summary>
    public DateTime PublishDate { get; set; }

    /// <summary>
    /// The release size in bytes.
    /// </summary>
    public long? Size { get; set; }

    /// <summary>
    /// The URL to view release information.
    /// </summary>
    public Uri? InfoUrl { get; set; }

    /// <summary>
    /// A list of categories associated with this release.
    /// </summary>
    /// <remarks>
    /// The list of categories is stored as serialized JSON.
    /// </remarks>
    public List<string> Categories { get; set; } = [];

    /// <summary>
    /// An ordered list of languages associated with this release.
    /// </summary>
    /// <remarks>
    /// The list of languages is stored as serialized JSON.
    /// </remarks>
    public List<string> Languages { get; set; } = [];

    /// <summary>
    /// Provider-specific properties for the release.
    /// </summary>
    /// <remarks>
    /// The properties are stored as serialized JSON.
    /// </remarks>
    public string? Properties { get; set; }

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
    /// The <see cref="Download"/> entries associated with the release.
    /// </summary>
    public ICollection<Download> Downloads { get; } = [];
}
