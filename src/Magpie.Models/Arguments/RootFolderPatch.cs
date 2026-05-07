using System;

using Magpie.Models.Data;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="RootFolder"/>.
/// </summary>
public sealed record RootFolderPatch
{
    /// <summary>
    /// The <see cref="RootFolder.Name"/> to patch.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// The <see cref="RootFolder.Path"/> to patch.
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// The <see cref="RootFolder.ContentTypeId"/> to patch.
    /// </summary>
    public Guid? ContentTypeId { get; init; }
}
