using System;

namespace Magpie.Models.Data;

/// <summary>
/// One content hierarchy node in the scope of a download.
/// </summary>
public record DownloadScope
{
    /// <summary>
    /// The unique id of the download scope.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the download.
    /// </summary>
    public Guid DownloadId { get; set; }

    /// <summary>
    /// The id of the content hierarchy item.
    /// </summary>
    public Guid ContentHierarchyItemId { get; set; }

    /// <summary>
    /// The related download.
    /// </summary>
    public Download? Download { get; set; }

    /// <summary>
    /// The related <see cref="ContentHierarchyItem"/>.
    /// </summary>
    public ContentHierarchyItem? ContentHierarchyItem { get; set; }
}
