using System.Collections.Generic;

namespace Magpie.Models.UI;

/// <summary>
/// The UI manifest of all extension assets and slot contributions.
/// </summary>
public sealed record UIManifest
{
    /// <summary>
    /// The UI assets from all extensions.
    /// </summary>
    public required IReadOnlyList<Asset> Assets { get; init; }

    /// <summary>
    /// The UI slot contributions from all extensions.
    /// </summary>
    public required IReadOnlyList<UIExtension> Extensions { get; init; }
}
