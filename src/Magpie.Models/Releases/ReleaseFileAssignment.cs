using System;

namespace Magpie.Models.Releases;

/// <summary>
/// The content hierarchy assignment of a release file.
/// </summary>
public sealed record ReleaseFileAssignment
{
    /// <summary>
    /// The release file key.
    /// </summary>
    public required string FileKey { get; init; }

    /// <summary>
    /// The assigned leaf id, or <see langword="null"/> when no assignment can be confirmed.
    /// </summary>
    public Guid? LeafContentHierarchyItemId { get; init; }
}
