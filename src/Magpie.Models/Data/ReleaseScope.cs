using System;

using Magpie.Models.Enums;

namespace Magpie.Models.Data;

/// <summary>
/// A content hierarchy node that a release covers.
/// </summary>
public record ReleaseScope
{
    /// <summary>
    /// The unique id of the release scope.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the scoped release.
    /// </summary>
    public Guid ReleaseId { get; set; }

    /// <summary>
    /// The id of the content hierarchy item in the scope.
    /// </summary>
    public Guid ContentHierarchyItemId { get; set; }

    /// <summary>
    /// The status of the release in the scope of the content hierarchy item.
    /// </summary>
    public ReleaseScopeStatus Status { get; set; }

    /// <summary>
    /// The <see cref="Release"/> in the scope.
    /// </summary>
    public Release? Release { get; set; }

    /// <summary>
    /// The <see cref="ContentHierarchyItem"/> in the scope.
    /// </summary>
    public ContentHierarchyItem? ContentHierarchyItem { get; set; }
}
