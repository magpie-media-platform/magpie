using System;
using System.Collections.Generic;

namespace Magpie.Models.Api;

/// <summary>
/// Selects files under a content parent for explicit renaming.
/// </summary>
public sealed record FileRenameSelection
{
    /// <summary>
    /// The selected associated files, or null for all descendant files.
    /// </summary>
    public IReadOnlyList<Guid>? MediaFileIds { get; init; }

    /// <summary>
    /// An unassociated file to preview before manual association.
    /// </summary>
    public string? FilePath { get; init; }
}
