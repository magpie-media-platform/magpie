using System.Collections.Generic;

namespace Magpie.Models.Api;

/// <summary>
/// The preview or outcome of explicit file renaming.
/// </summary>
public sealed record FileRenamePreview
{
    /// <summary>
    /// Whether any selected file has an applicable rename mask.
    /// </summary>
    public bool HasMask { get; init; }

    /// <summary>
    /// The individual file plans or outcomes.
    /// </summary>
    public IReadOnlyList<FileRenameEntry> Files { get; init; } = [];
}
